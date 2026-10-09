import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Subject,
  catchError,
  filter,
  finalize,
  interval,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  CALENDAR_MAX_DAYS,
  CALENDAR_MAX_RANGE_MS,
  CalendarQuery,
  Reservation,
} from '../../core/models/reservation.model';
import { RESOURCE_STATUSES, Resource, ResourceType } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import {
  addDays,
  formatDuration,
  parseDateParam,
  startOfDay,
  toDateParam,
} from '../../shared/utils/date-time';
import { EventBuffer } from '../../shared/utils/event-buffer';
import { searchKey } from '../../shared/utils/search';
import { resourceTypeLabel } from '../../core/i18n/labels';

const DEFAULT_DAYS = 7;

interface LiveEvent {
  cancelled: boolean;
  reservation: Reservation;
}

interface Row {
  reservation: Reservation;
  /** "Mon 5 Oct, 10:00-11:30", or with both dates when it spans days. */
  when: string;
  duration: string;
  bookedBy: string;
  /** Whose it is, for sentences: null when it's the user's own. */
  owner: string | null;
  inProgress: boolean;
  ended: boolean;
}

/**
 * Admin overview: resource status counts, and every Confirmed reservation in a date range,
 * which an admin can change or cancel whoever booked it. The range and type live in the
 * query string; the search stays on the page. Kept current by the reservation hub.
 */
@Component({
  imports: [RouterLink, StatusBadge],
  selector: 'app-admin',
  styleUrl: './admin.scss',
  templateUrl: './admin.html',
})
export class Admin {
  protected readonly typeLabel = resourceTypeLabel;

  private readonly reservationService = inject(ReservationService);
  private readonly resourceService = inject(ResourceService);
  private readonly hub = inject(ReservationHubService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);

  /** Query parameters: first and last day ("2026-10-05"), inclusive, and a resource type id. */
  readonly from = input<string>();
  readonly to = input<string>();
  readonly type = input<string>();

  protected readonly statuses = RESOURCE_STATUSES;
  protected readonly types = signal<ResourceType[]>([]);
  /** Null until loaded, so the counts don't read "0" meanwhile. */
  private readonly resources = signal<Resource[] | null>(null);
  protected readonly resourcesFailed = signal(false);
  private readonly reservations = signal<Reservation[]>([]);
  protected readonly search = signal('');
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the reservation being cancelled, to disable its button meanwhile. */
  protected readonly cancellingId = signal<string | null>(null);
  private readonly retries = new Subject<void>();
  private readonly liveEvents = new EventBuffer<LiveEvent>();

  // Re-evaluated every minute for "in progress" and which reservations can still be cancelled.
  private readonly now = signal(Date.now());

  protected readonly range = computed(
    () => {
      const start = parseDateParam(this.from()) ?? startOfDay(new Date(this.now()));
      const last = parseDateParam(this.to()) ?? addDays(start, DEFAULT_DAYS - 1);
      return { start, last, end: addDays(last, 1) };
    },
    {
      // By value, so the per-minute clock doesn't count as a new range and refetch.
      equal: (a, b) =>
        a.start.getTime() === b.start.getTime() && a.last.getTime() === b.last.getTime(),
    },
  );
  protected readonly rangeError = computed(() => {
    const { start, last, end } = this.range();
    if (last < start) {
      return $localize`:@@admin.lastBeforeFirst:The last day is before the first day.`;
    }
    if (end.getTime() - start.getTime() > CALENDAR_MAX_RANGE_MS) {
      return $localize`:@@admin.tooLong:Show at most ${CALENDAR_MAX_DAYS}:days: days at a time.`;
    }
    return null;
  });

  protected readonly statusCounts = computed(() => {
    const resources = this.resources();
    return resources
      ? this.statuses.map((status) => ({
          status,
          count: resources.filter((r) => r.status === status).length,
        }))
      : null;
  });

  protected readonly rows = computed((): Row[] => {
    const now = this.now();
    const userId = this.authService.currentUser()?.id;
    const term = searchKey(this.search().trim());
    return this.reservations()
      .filter(
        (r) =>
          !term ||
          [r.resourceName, r.resourceLocation, r.userDisplayName ?? '', r.note ?? ''].some((text) =>
            searchKey(text).includes(term),
          ),
      )
      .sort((a, b) => a.startDateTime.localeCompare(b.startDateTime))
      .map((reservation) => {
        const start = new Date(reservation.startDateTime);
        const end = new Date(reservation.endDateTime);
        const mine = reservation.userId === userId;
        const name = reservation.userDisplayName ?? $localize`:@@common.unknownUser:Unknown user`;
        return {
          reservation,
          when: this.when(start, end),
          duration: formatDuration(start, end),
          bookedBy: mine ? $localize`:@@common.you:You` : name,
          owner: mine ? null : name,
          inProgress: start.getTime() <= now && end.getTime() > now,
          ended: end.getTime() <= now,
        };
      });
  });

  protected readonly total = computed(() => this.reservations().length);

  /** "12 confirmed reservations from Mon 5 Oct to Sun 11 Oct.", or "3 of 12 ..." when searching. */
  protected readonly summary = computed(() => {
    const total = this.total();
    const shown = this.rows().length;
    const from = formatDate(this.range().start, 'EEE d MMM', this.locale);
    const to = formatDate(this.range().last, 'EEE d MMM', this.locale);
    if (this.search().trim()) {
      return total === 1
        ? $localize`:@@admin.summaryFilteredOne:${shown}:shown: of 1 confirmed reservation from ${from}:from: to ${to}:to:.`
        : $localize`:@@admin.summaryFiltered:${shown}:shown: of ${total}:total: confirmed reservations from ${from}:from: to ${to}:to:.`;
    }
    return total === 1
      ? $localize`:@@admin.summaryOne:1 confirmed reservation from ${from}:from: to ${to}:to:.`
      : $localize`:@@admin.summary:${total}:total: confirmed reservations from ${from}:from: to ${to}:to:.`;
  });

  constructor() {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });
    this.resourceService
      .getResources()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (resources) => this.resources.set(resources),
        error: () => this.resourcesFailed.set(true),
      });

    const query = computed(
      (): CalendarQuery | null =>
        this.rangeError()
          ? null
          : {
              from: this.range().start.toISOString(),
              to: this.range().end.toISOString(),
              resourceTypeId: this.type() || null,
            },
      { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
    );
    merge(
      toObservable(query),
      // Events sent while the connection was down are lost; catch up from the API.
      this.hub.reconnected$.pipe(map(() => query())),
      this.retries.pipe(map(() => query())),
    )
      .pipe(
        filter((q): q is CalendarQuery => q !== null),
        tap(() => {
          this.loadFailed.set(false);
          this.liveEvents.start();
        }),
        switchMap((q) => this.reservationService.getCalendar(q).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((reservations) => {
        this.loading.set(false);
        const missed = this.liveEvents.drain();
        if (reservations) {
          this.reservations.set(reservations);
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

  /** "2026-10-05", for the date inputs. */
  protected dateValue(date: Date): string {
    return toDateParam(date);
  }

  protected setDay(param: 'from' | 'to', event: Event): void {
    this.navigate({ [param]: (event.target as HTMLInputElement).value || null });
  }

  protected selectType(event: Event): void {
    this.navigate({ type: (event.target as HTMLSelectElement).value || null });
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected retry(): void {
    this.loading.set(true);
    this.retries.next();
  }

  protected cancel(row: Row): void {
    const { reservation } = row;
    const resource = reservation.resourceName;
    const owner = row.owner;
    const when = row.when;
    const question = owner
      ? $localize`:@@reservationForm.confirmCancelOther:Cancel ${owner}:owner:'s ${resource}:resource: reservation for ${when}:when:?`
      : $localize`:@@reservationForm.confirmCancel:Cancel your ${resource}:resource: reservation for ${when}:when:?`;
    if (!confirm(question)) {
      return;
    }

    this.cancellingId.set(reservation.id);
    this.reservationService
      .cancel(reservation.id)
      .pipe(finalize(() => this.cancellingId.set(null)))
      .subscribe({
        next: (cancelled) => {
          this.remove(cancelled.id);
          const name = cancelled.resourceName;
          this.notifications.success(
            owner
              ? $localize`:@@reservationForm.cancelledOther:${owner}:owner:'s ${name}:resource: reservation for ${when}:when: was cancelled.`
              : $localize`:@@reservationForm.cancelled:Your ${name}:resource: reservation for ${when}:when: was cancelled.`,
          );
        },
        error: (error: unknown) => {
          // 409: it ended in the meantime. 403/404/5xx already raised a toast.
          if (error instanceof ApiError && error.status === 409) {
            this.notifications.error(error.detail ?? error.title);
          }
          this.retries.next(); // show the list as it is now
        },
      });
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  /**
   * Created or changed: show it if it's Confirmed and matches the range and type, drop it
   * otherwise (a change can move it out). Cancelled: drop it.
   */
  private apply({ cancelled, reservation }: LiveEvent): void {
    if (!cancelled && this.isInView(reservation)) {
      this.reservations.update((list) => [
        ...list.filter((r) => r.id !== reservation.id),
        reservation,
      ]);
    } else {
      this.remove(reservation.id);
    }
  }

  private isInView(reservation: Reservation): boolean {
    const { start, end } = this.range();
    const type = this.type();
    return (
      reservation.status === 'Confirmed' &&
      new Date(reservation.startDateTime) < end &&
      new Date(reservation.endDateTime) > start &&
      (!type || reservation.resourceTypeId === type)
    );
  }

  private remove(id: string): void {
    this.reservations.update((list) => list.filter((r) => r.id !== id));
  }

  private when(start: Date, end: Date): string {
    const sameDay = start.toDateString() === end.toDateString();
    const from = formatDate(start, 'EEE d MMM, HH:mm', this.locale);
    const to = formatDate(end, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
    return sameDay ? `${from}-${to}` : $localize`:@@common.rangeTo:${from}:start: to ${to}:end:`;
  }
}
