import { DatePipe, formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, forkJoin, interval, map, merge, of, switchMap, tap } from 'rxjs';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource, ResourceType } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import {
  addDays,
  addMinutes,
  addMonths,
  nextHalfHour,
  parseDateParam,
  parseMonthParam,
  startOfMonth,
  startOfWeek,
  toDateParam,
  toMonthParam,
} from '../../shared/utils/date-time';
import { EventBuffer } from '../../shared/utils/event-buffer';

const WEEK_DAYS = 7;
const DEFAULT_BOOKING_HOUR = 9;
const DEFAULT_BOOKING_MINUTES = 60;
/** A day of the month view lists this many entries; the rest are behind "+N more". */
const MONTH_ENTRIES_SHOWN = 3;

type View = 'week' | 'month';

interface Day {
  key: string;
  start: Date;
  end: Date;
  isToday: boolean;
  isPast: boolean;
}

interface Entry {
  reservation: Reservation;
  /** "09:00-10:30", or "from 22:00" / "until 09:00" / "All day" for a part of a longer one. */
  time: string;
  who: string;
  mine: boolean;
  title: string;
}

/** A blocked window, drawn in every day it covers. */
interface BlockEntry {
  block: ResourceBlock;
  time: string;
  title: string;
}

interface Cell {
  day: Day;
  blocks: BlockEntry[];
  entries: Entry[];
  /** Query parameters for booking this resource on this day; null when it can't be booked. */
  book: { params: Record<string, string>; label: string } | null;
}

interface Row {
  resource: Resource;
  cells: Cell[];
}

/** One entry in a day of the month view: a reservation or a block, with its resource. */
interface MonthItem {
  key: string;
  resourceName: string;
  entry: Entry | null;
  block: BlockEntry | null;
}

interface MonthDay {
  day: Day;
  /** Days of the previous and next month fill the first and last week, and stay empty. */
  inMonth: boolean;
  items: MonthItem[];
  /** How many more entries the day has than it lists. */
  more: number;
  /** Query parameters that open the week view on this day. */
  weekParams: Record<string, string | null>;
}

/** A reservation event from the hub. */
interface LiveEvent {
  cancelled: boolean;
  reservation: Reservation;
}

/**
 * The shared office calendar, showing who booked what and when resources are blocked.
 * - Week view: a row per resource and a column per day. Entries open the reservation
 *   (editable for its owner or an admin, read-only for everyone else); "+" starts a booking
 *   of that resource on that day.
 * - Month view: a day per cell with its first few entries; a day opens its week.
 * The view, week or month, and type live in the query string. Reservations are kept current
 * by the hub; blocks send no events, so they're fetched with the range and after a reconnect.
 */
@Component({
  imports: [RouterLink, DatePipe, StatusBadge],
  selector: 'app-calendar',
  styleUrl: './calendar.scss',
  templateUrl: './calendar.html',
})
export class Calendar {
  private readonly reservationService = inject(ReservationService);
  private readonly resourceService = inject(ResourceService);
  private readonly blockService = inject(ResourceBlockService);
  private readonly hub = inject(ReservationHubService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);

  /**
   * Query parameters: "month" for the month view (the week view otherwise), any day of the
   * week to show ("2026-10-07"), the month to show ("2026-10"), and a resource type id.
   */
  readonly view = input<string>();
  readonly week = input<string>();
  readonly month = input<string>();
  readonly type = input<string>();

  protected readonly types = signal<ResourceType[]>([]);
  private readonly resources = signal<Resource[]>([]);
  private readonly reservations = signal<Reservation[]>([]);
  private readonly blocks = signal<ResourceBlock[]>([]);
  protected readonly blocksFailed = signal(false);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  private readonly retries = new Subject<void>();
  private readonly liveEvents = new EventBuffer<LiveEvent>();

  // Re-evaluated every minute for "today", past days and the booking links.
  private readonly now = signal(Date.now());

  protected readonly mode = computed((): View => (this.view() === 'month' ? 'month' : 'week'));

  // Compared by time, so the per-minute clock doesn't count as a new week or month and refetch.
  protected readonly weekStart = computed(
    () => startOfWeek(parseDateParam(this.week()) ?? new Date(this.now())),
    { equal: sameTime },
  );
  protected readonly monthStart = computed(
    () => startOfMonth(parseMonthParam(this.month()) ?? new Date(this.now())),
    { equal: sameTime },
  );

  /**
   * What's fetched and shown: the week, or the month's own days. A month is at most 31 days,
   * well inside the API's 42, even when a daylight-saving change makes a day 25 hours long.
   */
  private readonly range = computed(
    () =>
      this.mode() === 'month'
        ? { start: this.monthStart(), end: addMonths(this.monthStart(), 1) }
        : { start: this.weekStart(), end: addDays(this.weekStart(), WEEK_DAYS) },
    { equal: (a, b) => sameTime(a.start, b.start) && sameTime(a.end, b.end) },
  );

  protected readonly isCurrent = computed(() => {
    const now = new Date(this.now());
    return this.mode() === 'month'
      ? sameTime(this.monthStart(), startOfMonth(now))
      : sameTime(this.weekStart(), startOfWeek(now));
  });
  protected readonly label = computed(() => {
    if (this.mode() === 'month') {
      return formatDate(this.monthStart(), 'MMMM y', this.locale);
    }
    const start = this.weekStart();
    const last = addDays(start, WEEK_DAYS - 1);
    const sameMonth = start.getMonth() === last.getMonth();
    const from = formatDate(start, sameMonth ? 'd' : 'd MMM', this.locale);
    return `${from} - ${formatDate(last, 'd MMM y', this.locale)}`;
  });

  protected readonly days = computed((): Day[] => {
    const now = this.now();
    return Array.from({ length: WEEK_DAYS }, (_, i) => makeDay(addDays(this.weekStart(), i), now));
  });

  private readonly sortedResources = computed(() =>
    [...this.resources()].sort(
      (a, b) =>
        a.resourceTypeName.localeCompare(b.resourceTypeName, 'en') ||
        a.name.localeCompare(b.name, 'en'),
    ),
  );

  /** Blocks of the resources shown: the blocks query has no type filter. */
  private readonly visibleBlocks = computed(() => {
    const shown = new Set(this.resources().map((r) => r.id));
    return this.blocks().filter((b) => shown.has(b.resourceId));
  });

  protected readonly rows = computed((): Row[] => {
    const days = this.days();
    const reservations = groupBy(this.reservations(), (r) => r.resourceId);
    const blocks = groupBy(this.visibleBlocks(), (b) => b.resourceId);

    return this.sortedResources().map((resource) => ({
      resource,
      cells: days.map((day) => ({
        day,
        blocks: (blocks.get(resource.id) ?? [])
          .filter((b) => overlaps(b, day.start, day.end))
          .sort(byStart)
          .map((b) => this.blockEntry(b, day)),
        entries: (reservations.get(resource.id) ?? [])
          .filter((r) => overlaps(r, day.start, day.end))
          .sort(byStart)
          .map((r) => this.entry(r, day)),
        book: this.bookLink(resource, day),
      })),
    }));
  });

  protected readonly weekdays = computed(() =>
    Array.from({ length: WEEK_DAYS }, (_, i) =>
      formatDate(addDays(startOfWeek(this.monthStart()), i), 'EEE', this.locale),
    ),
  );

  protected readonly monthWeeks = computed((): MonthDay[][] => {
    const now = this.now();
    const first = this.monthStart();
    const next = addMonths(first, 1);
    const weeks: MonthDay[][] = [];
    for (let monday = startOfWeek(first); monday < next; monday = addDays(monday, WEEK_DAYS)) {
      weeks.push(
        Array.from({ length: WEEK_DAYS }, (_, i) => {
          const day = makeDay(addDays(monday, i), now);
          const inMonth = day.start >= first && day.start < next;
          const items = inMonth ? this.monthItems(day) : [];
          return {
            day,
            inMonth,
            items: items.slice(0, MONTH_ENTRIES_SHOWN),
            more: Math.max(0, items.length - MONTH_ENTRIES_SHOWN),
            weekParams: { view: null, month: null, week: day.key },
          };
        }),
      );
    }
    return weeks;
  });

  protected readonly hasResources = computed(() => this.resources().length > 0);
  protected readonly reservationCount = computed(() => this.reservations().length);
  protected readonly blockCount = computed(() => this.visibleBlocks().length);

  constructor() {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });

    const query = computed(() => ({ range: this.range(), type: this.type() || null }));
    merge(
      toObservable(query),
      // Events sent while the connection was down are lost, and blocks send none: catch up
      // from the API.
      this.hub.reconnected$.pipe(map(() => query())),
      this.retries.pipe(map(() => query())),
    )
      .pipe(
        tap(() => {
          this.loadFailed.set(false);
          this.liveEvents.start();
        }),
        switchMap(({ range, type }) => {
          const from = range.start.toISOString();
          const to = range.end.toISOString();
          return forkJoin({
            resources: this.resourceService.getResources({ resourceTypeId: type }),
            reservations: this.reservationService.getCalendar({ from, to, resourceTypeId: type }),
            // The calendar is still worth showing without them.
            blocks: this.blockService.getBlocks({ from, to }).pipe(catchError(() => of(null))),
          }).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        const missed = this.liveEvents.drain();
        if (result) {
          this.resources.set(result.resources);
          this.reservations.set(result.reservations);
          this.blocks.set(result.blocks ?? []);
          this.blocksFailed.set(result.blocks === null);
          // The result may predate changes that arrived while it loaded.
          missed.forEach((event) => this.apply(event));
        } else {
          this.loadFailed.set(true);
        }
      });

    merge(
      this.hub.created$.pipe(map((reservation) => ({ cancelled: false, reservation }))),
      this.hub.updated$.pipe(map((reservation) => ({ cancelled: false, reservation }))),
      this.hub.cancelled$.pipe(map((reservation) => ({ cancelled: true, reservation }))),
    )
      .pipe(takeUntilDestroyed())
      .subscribe((event) => {
        this.apply(event);
        this.liveEvents.record(event);
      });

    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));
  }

  /** The previous or next week or month. */
  protected go(offset: number): void {
    if (this.mode() === 'month') {
      this.navigate({ month: toMonthParam(addMonths(this.monthStart(), offset)) });
    } else {
      this.navigate({ week: toDateParam(addDays(this.weekStart(), offset * WEEK_DAYS)) });
    }
  }

  protected goToCurrent(): void {
    this.navigate(this.mode() === 'month' ? { month: null } : { week: null });
  }

  /**
   * Week to month: the month that holds most of the week shown (its Thursday's). Month to
   * week: the month's first week. From the current week or month, to the current one.
   */
  protected showView(view: View): void {
    if (view === this.mode()) {
      return;
    }
    if (view === 'month') {
      const month = this.isCurrent() ? null : toMonthParam(addDays(this.weekStart(), 3));
      this.navigate({ view: 'month', month, week: null });
    } else {
      const week = this.isCurrent() ? null : toDateParam(this.monthStart());
      this.navigate({ view: null, week, month: null });
    }
  }

  protected selectType(event: Event): void {
    this.navigate({ type: (event.target as HTMLSelectElement).value || null });
  }

  protected retry(): void {
    this.loading.set(true);
    this.retries.next();
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  /**
   * Created or changed: show it if it's Confirmed and in view, drop it otherwise (a change
   * can move it to another week, month or resource). Cancelled: drop it.
   */
  private apply({ cancelled, reservation }: LiveEvent): void {
    if (!cancelled && this.isInView(reservation)) {
      this.upsert(reservation);
    } else {
      this.remove(reservation.id);
    }
  }

  private isInView(reservation: Reservation): boolean {
    const { start, end } = this.range();
    return (
      reservation.status === 'Confirmed' &&
      overlaps(reservation, start, end) &&
      this.resources().some((r) => r.id === reservation.resourceId)
    );
  }

  private upsert(reservation: Reservation): void {
    this.reservations.update((list) => [
      ...list.filter((r) => r.id !== reservation.id),
      reservation,
    ]);
  }

  private remove(id: string): void {
    this.reservations.update((list) => list.filter((r) => r.id !== id));
  }

  /** A day's reservations and blocks for the month view, earliest first. */
  private monthItems(day: Day): MonthItem[] {
    const blocks = this.visibleBlocks()
      .filter((b) => overlaps(b, day.start, day.end))
      .map((b) => ({
        start: b.startDateTime,
        name: b.resourceName,
        block: this.blockEntry(b, day),
      }));
    const reservations = this.reservations()
      .filter((r) => overlaps(r, day.start, day.end))
      .map((r) => ({ start: r.startDateTime, name: r.resourceName, entry: this.entry(r, day) }));
    return [...blocks, ...reservations]
      .sort((a, b) => a.start.localeCompare(b.start) || a.name.localeCompare(b.name, 'en'))
      .map((item) => ({
        key: 'block' in item ? `block-${item.block.block.id}` : item.entry.reservation.id,
        resourceName: item.name,
        entry: 'entry' in item ? item.entry : null,
        block: 'block' in item ? item.block : null,
      }));
  }

  private entry(reservation: Reservation, day: Day): Entry {
    const start = new Date(reservation.startDateTime);
    const end = new Date(reservation.endDateTime);
    const mine = reservation.userId === this.authService.currentUser()?.id;
    const who = mine ? 'You' : (reservation.userDisplayName ?? 'Someone');
    const note = reservation.note ? ` - ${reservation.note}` : '';
    return {
      reservation,
      mine,
      who,
      time: this.timeInDay(start, end, day),
      title: `${this.fullRange(start, end)}, ${who}${note}`,
    };
  }

  private blockEntry(block: ResourceBlock, day: Day): BlockEntry {
    const start = new Date(block.startDateTime);
    const end = new Date(block.endDateTime);
    return {
      block,
      time: this.timeInDay(start, end, day),
      title: `${block.resourceName} is blocked ${this.fullRange(start, end)}: ${block.reason}`,
    };
  }

  /** "09:00-10:30", or the part of a longer range that falls on `day`. */
  private timeInDay(start: Date, end: Date, day: Day): string {
    const time = (date: Date) => formatDate(date, 'HH:mm', this.locale);
    const startsBefore = start < day.start;
    const endsAfter = end > day.end;
    if (startsBefore && endsAfter) return 'All day';
    if (startsBefore) return `until ${time(end)}`;
    if (endsAfter) return `from ${time(start)}`;
    return `${time(start)}-${time(end)}`;
  }

  private fullRange(start: Date, end: Date): string {
    const full = (date: Date) => formatDate(date, 'EEE d MMM, HH:mm', this.locale);
    return `${full(start)} to ${full(end)}`;
  }

  /** Book at 09:00 on a future day, or at the next half hour today. */
  private bookLink(resource: Resource, day: Day): Cell['book'] {
    const now = this.now();
    if (resource.status !== 'Active' || day.isPast) {
      return null;
    }
    let start: Date;
    if (day.isToday) {
      start = nextHalfHour(new Date(now));
    } else {
      start = new Date(day.start);
      start.setHours(DEFAULT_BOOKING_HOUR, 0, 0, 0);
    }
    if (start >= day.end) {
      return null; // after 23:30 today
    }
    return {
      params: {
        resourceId: resource.id,
        start: start.toISOString(),
        end: addMinutes(start, DEFAULT_BOOKING_MINUTES).toISOString(),
      },
      label: `Book ${resource.name} on ${formatDate(day.start, 'EEE d MMM', this.locale)}`,
    };
  }
}

function makeDay(start: Date, now: number): Day {
  const end = addDays(start, 1);
  return {
    key: toDateParam(start),
    start,
    end,
    isToday: start.getTime() <= now && now < end.getTime(),
    isPast: end.getTime() <= now,
  };
}

function sameTime(a: Date, b: Date): boolean {
  return a.getTime() === b.getTime();
}

interface Timed {
  startDateTime: string;
  endDateTime: string;
}

function overlaps(item: Timed, from: Date, to: Date): boolean {
  return new Date(item.startDateTime) < to && new Date(item.endDateTime) > from;
}

function byStart(a: Timed, b: Timed): number {
  return a.startDateTime.localeCompare(b.startDateTime);
}

function groupBy<T>(items: T[], key: (item: T) => string): Map<string, T[]> {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    groups.set(key(item), [...(groups.get(key(item)) ?? []), item]);
  }
  return groups;
}
