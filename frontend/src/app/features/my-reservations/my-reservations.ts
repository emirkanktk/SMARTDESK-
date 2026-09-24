import { DatePipe, formatDate } from '@angular/common';
import { Component, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { filter, finalize, interval, merge } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { formatDuration } from '../../shared/utils/date-time';

type Tab = 'upcoming' | 'past' | 'cancelled';

interface Item {
  reservation: Reservation;
  start: Date;
  end: Date;
  sameDay: boolean;
  duration: string;
  inProgress: boolean;
}

/**
 * The signed-in user's reservations: upcoming (including in progress), past and cancelled.
 * Upcoming ones can be changed or cancelled. Kept current by the reservation hub, so a
 * booking made or cancelled elsewhere shows up without a reload.
 */
@Component({
  imports: [RouterLink, DatePipe],
  selector: 'app-my-reservations',
  styleUrl: './my-reservations.scss',
  templateUrl: './my-reservations.html',
})
export class MyReservations implements OnInit {
  private readonly reservationService = inject(ReservationService);
  private readonly hub = inject(ReservationHubService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'upcoming', label: 'Upcoming' },
    { id: 'past', label: 'Past' },
    { id: 'cancelled', label: 'Cancelled' },
  ];
  protected readonly activeTab = signal<Tab>('upcoming');

  private readonly reservations = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the reservation being cancelled, to disable its button meanwhile. */
  protected readonly cancellingId = signal<string | null>(null);

  // Re-evaluated every minute, so a reservation moves to Past once it ends.
  private readonly now = signal(Date.now());

  private readonly items = computed(() => {
    const now = this.now();
    return this.reservations().map((reservation): Item => {
      const start = new Date(reservation.startDateTime);
      const end = new Date(reservation.endDateTime);
      return {
        reservation,
        start,
        end,
        sameDay: start.toDateString() === end.toDateString(),
        duration: formatDuration(start, end),
        inProgress:
          reservation.status === 'Confirmed' && start.getTime() <= now && end.getTime() > now,
      };
    });
  });

  private readonly grouped = computed(() => {
    const now = this.now();
    const confirmed = this.items().filter((i) => i.reservation.status === 'Confirmed');
    return {
      // Soonest first: what's next matters most.
      upcoming: confirmed.filter((i) => i.end.getTime() > now).sort(byStart),
      // Most recent first.
      past: confirmed
        .filter((i) => i.end.getTime() <= now)
        .sort(byStart)
        .reverse(),
      cancelled: this.items()
        .filter((i) => i.reservation.status === 'Cancelled')
        .sort(byStart)
        .reverse(),
    };
  });

  protected readonly visible = computed(() => this.grouped()[this.activeTab()]);
  protected readonly counts = computed(() => {
    const g = this.grouped();
    return { upcoming: g.upcoming.length, past: g.past.length, cancelled: g.cancelled.length };
  });

  constructor() {
    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));

    const mine = (r: Reservation) => r.userId === this.authService.currentUser()?.id;
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(filter(mine), takeUntilDestroyed())
      .subscribe((reservation) => this.upsert(reservation));

    // Events sent while the connection was down are lost; catch up from the API.
    this.hub.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  ngOnInit(): void {
    this.load(true);
  }

  protected selectTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  protected reload(): void {
    this.load(true);
  }

  protected cancel(item: Item): void {
    const { reservation } = item;
    if (!confirm(`Cancel your ${reservation.resourceName} reservation for ${this.when(item)}?`)) {
      return;
    }

    this.cancellingId.set(reservation.id);
    this.reservationService
      .cancel(reservation.id)
      .pipe(finalize(() => this.cancellingId.set(null)))
      .subscribe({
        next: (cancelled) => {
          this.upsert(cancelled);
          this.notifications.success(
            `Your ${cancelled.resourceName} reservation for ${this.when(item)} was cancelled.`,
          );
        },
        error: (error: unknown) => {
          // 409: it ended in the meantime. 403/404/5xx already raised a toast.
          if (error instanceof ApiError && error.status === 409) {
            this.notifications.error(error.detail ?? error.title);
          }
          this.load(false); // whatever happened, show the reservation as it is now
        },
      });
  }

  /** "Mon 5 Oct, 10:00-11:30", or with both dates when it spans days. */
  private when(item: Item): string {
    const start = formatDate(item.start, 'EEE d MMM, HH:mm', this.locale);
    const end = formatDate(item.end, item.sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
    return item.sameDay ? `${start}-${end}` : `${start} to ${end}`;
  }

  /**
   * @param showLoading false to refresh quietly, keeping the current list on screen until
   * the new one arrives.
   */
  private load(showLoading: boolean): void {
    if (showLoading) {
      this.loading.set(true);
    }
    this.reservationService
      .getMine()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (reservations) => {
          this.reservations.set(reservations);
          this.loadFailed.set(false);
          this.now.set(Date.now());
        },
        error: () => {
          if (showLoading) {
            this.loadFailed.set(true);
          }
        },
      });
  }

  private upsert(reservation: Reservation): void {
    this.reservations.update((list) =>
      list.some((r) => r.id === reservation.id)
        ? list.map((r) => (r.id === reservation.id ? reservation : r))
        : [...list, reservation],
    );
  }
}

function byStart(a: Item, b: Item): number {
  return a.start.getTime() - b.start.getTime();
}
