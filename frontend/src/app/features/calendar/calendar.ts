import { DatePipe, formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, forkJoin, interval, map, merge, of, switchMap, tap } from 'rxjs';
import { Reservation } from '../../core/models/reservation.model';
import { Resource, ResourceType } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import {
  addDays,
  addMinutes,
  nextHalfHour,
  parseDateParam,
  startOfWeek,
  toDateParam,
} from '../../shared/utils/date-time';
import { EventBuffer } from '../../shared/utils/event-buffer';

const DAYS_SHOWN = 7;
const DEFAULT_BOOKING_HOUR = 9;
const DEFAULT_BOOKING_MINUTES = 60;

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

interface Cell {
  day: Day;
  entries: Entry[];
  /** Query parameters for booking this resource on this day; null when it can't be booked. */
  book: { params: Record<string, string>; label: string } | null;
}

interface Row {
  resource: Resource;
  cells: Cell[];
}

/** A reservation event from the hub. */
interface LiveEvent {
  cancelled: boolean;
  reservation: Reservation;
}

/**
 * The shared office calendar: one week, a row per resource and a column per day, showing who
 * booked what. Entries open the reservation (editable for its owner or an admin, read-only
 * for everyone else); "+" starts a booking of that resource on that day. The week and type
 * live in the query string. Kept current by the reservation hub.
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
  private readonly hub = inject(ReservationHubService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);

  /** Query parameters: any day of the week to show ("2026-10-07"), and a resource type id. */
  readonly week = input<string>();
  readonly type = input<string>();

  protected readonly types = signal<ResourceType[]>([]);
  private readonly resources = signal<Resource[]>([]);
  private readonly reservations = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  private readonly retries = new Subject<void>();
  private readonly liveEvents = new EventBuffer<LiveEvent>();

  // Re-evaluated every minute for "today", past days and the booking links.
  private readonly now = signal(Date.now());

  // Compared by time, so the per-minute clock doesn't count as a new week and refetch.
  protected readonly weekStart = computed(
    () => startOfWeek(parseDateParam(this.week()) ?? new Date(this.now())),
    { equal: (a, b) => a.getTime() === b.getTime() },
  );
  protected readonly isThisWeek = computed(
    () => this.weekStart().getTime() === startOfWeek(new Date(this.now())).getTime(),
  );
  protected readonly weekLabel = computed(() => {
    const start = this.weekStart();
    const last = addDays(start, DAYS_SHOWN - 1);
    const sameMonth = start.getMonth() === last.getMonth();
    const from = formatDate(start, sameMonth ? 'd' : 'd MMM', this.locale);
    return `${from} - ${formatDate(last, 'd MMM y', this.locale)}`;
  });

  protected readonly days = computed((): Day[] => {
    const now = this.now();
    return Array.from({ length: DAYS_SHOWN }, (_, i) => {
      const start = addDays(this.weekStart(), i);
      const end = addDays(start, 1);
      return {
        key: toDateParam(start),
        start,
        end,
        isToday: start.getTime() <= now && now < end.getTime(),
        isPast: end.getTime() <= now,
      };
    });
  });

  protected readonly rows = computed((): Row[] => {
    const days = this.days();
    const byResource = new Map<string, Reservation[]>();
    for (const reservation of this.reservations()) {
      byResource.set(reservation.resourceId, [
        ...(byResource.get(reservation.resourceId) ?? []),
        reservation,
      ]);
    }

    return [...this.resources()]
      .sort(
        (a, b) =>
          a.resourceTypeName.localeCompare(b.resourceTypeName, 'en') ||
          a.name.localeCompare(b.name, 'en'),
      )
      .map((resource) => ({
        resource,
        cells: days.map((day) => ({
          day,
          entries: (byResource.get(resource.id) ?? [])
            .filter((r) => overlaps(r, day.start, day.end))
            .sort((a, b) => a.startDateTime.localeCompare(b.startDateTime))
            .map((r) => this.entry(r, day)),
          book: this.bookLink(resource, day),
        })),
      }));
  });

  protected readonly reservationCount = computed(() => this.reservations().length);

  constructor() {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });

    const query = computed(() => ({ from: this.weekStart(), type: this.type() || null }));
    merge(
      toObservable(query),
      // Events sent while the connection was down are lost; catch up from the API.
      this.hub.reconnected$.pipe(map(() => query())),
      this.retries.pipe(map(() => query())),
    )
      .pipe(
        tap(() => {
          this.loadFailed.set(false);
          this.liveEvents.start();
        }),
        switchMap(({ from, type }) =>
          forkJoin({
            resources: this.resourceService.getResources({ resourceTypeId: type }),
            reservations: this.reservationService.getCalendar({
              from: from.toISOString(),
              to: addDays(from, DAYS_SHOWN).toISOString(),
              resourceTypeId: type,
            }),
          }).pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        const missed = this.liveEvents.drain();
        if (result) {
          this.resources.set(result.resources);
          this.reservations.set(result.reservations);
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

  protected goToWeek(offset: number): void {
    this.navigate({ week: toDateParam(addDays(this.weekStart(), offset * DAYS_SHOWN)) });
  }

  protected goToThisWeek(): void {
    this.navigate({ week: null });
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
   * can move it to another week or resource). Cancelled: drop it.
   */
  private apply({ cancelled, reservation }: LiveEvent): void {
    if (!cancelled && this.isInView(reservation)) {
      this.upsert(reservation);
    } else {
      this.remove(reservation.id);
    }
  }

  private isInView(reservation: Reservation): boolean {
    const from = this.weekStart();
    return (
      reservation.status === 'Confirmed' &&
      overlaps(reservation, from, addDays(from, DAYS_SHOWN)) &&
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

  private entry(reservation: Reservation, day: Day): Entry {
    const start = new Date(reservation.startDateTime);
    const end = new Date(reservation.endDateTime);
    const time = (date: Date) => formatDate(date, 'HH:mm', this.locale);
    const startsBefore = start < day.start;
    const endsAfter = end > day.end;
    const mine = reservation.userId === this.authService.currentUser()?.id;
    const who = mine ? 'You' : (reservation.userDisplayName ?? 'Someone');

    const full = (date: Date) => formatDate(date, 'EEE d MMM, HH:mm', this.locale);
    const note = reservation.note ? ` - ${reservation.note}` : '';
    return {
      reservation,
      mine,
      who,
      time:
        startsBefore && endsAfter
          ? 'All day'
          : startsBefore
            ? `until ${time(end)}`
            : endsAfter
              ? `from ${time(start)}`
              : `${time(start)}-${time(end)}`,
      title: `${full(start)} to ${full(end)}, ${who}${note}`,
    };
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

function overlaps(reservation: Reservation, from: Date, to: Date): boolean {
  return new Date(reservation.startDateTime) < to && new Date(reservation.endDateTime) > from;
}
