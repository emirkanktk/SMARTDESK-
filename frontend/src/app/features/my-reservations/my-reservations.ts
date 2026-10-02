import { DatePipe, formatDate } from '@angular/common';
import { Component, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  Subject,
  catchError,
  finalize,
  forkJoin,
  interval,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AttendeeResponse, Reservation, checkInWindow } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ExtendBooking } from '../../shared/components/extend-booking/extend-booking';
import { MeetingAnswer } from '../../shared/components/meeting-answer/meeting-answer';
import { formatDuration } from '../../shared/utils/date-time';
import { EventBuffer } from '../../shared/utils/event-buffer';

type Tab = 'upcoming' | 'invited' | 'past' | 'cancelled';

/** How many invited people of each answer a booking names before "and 3 more". */
export const ATTENDEES_SHOWN = 3;

interface Item {
  reservation: Reservation;
  start: Date;
  end: Date;
  sameDay: boolean;
  duration: string;
  inProgress: boolean;
  /** "Reminder 15 min before", while it can still come: Confirmed and not started. */
  reminder: string | null;
  /** "Check in at the door 09:50-10:15", while that's still to do (ADR-0030). */
  checkInHint: string | null;
  checkedIn: boolean;
  /** "Nobody checked in by 10:15", for a booking released because of it. */
  releasedNote: string | null;
  /**
   * "Coming: Ayşe Yılmaz · No answer yet: Mehmet Demir" on the user's own bookings that
   * invite anyone.
   */
  attendees: string | null;
  /** "Organized by Zeynep Kaya" on a meeting the user is invited to. */
  organizer: string | null;
}

/**
 * The signed-in user's reservations: upcoming (including in progress), past and cancelled,
 * and the meetings they're invited to that aren't over (ADR-0032). Upcoming ones can be
 * changed or cancelled. Kept current by the reservation hub, so a booking made, changed or
 * cancelled elsewhere - an invitation too - shows up without a reload.
 */
@Component({
  imports: [RouterLink, DatePipe, ExtendBooking, MeetingAnswer],
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
    { id: 'upcoming', label: $localize`:@@mine.tab.upcoming:Upcoming` },
    { id: 'invited', label: $localize`:@@mine.tab.invited:Invitations` },
    { id: 'past', label: $localize`:@@mine.tab.past:Past` },
    { id: 'cancelled', label: $localize`:@@status.reservation.cancelled:Cancelled` },
  ];
  protected readonly activeTab = signal<Tab>('upcoming');

  private readonly reservations = signal<Reservation[]>([]);
  /** Other people's meetings the user is invited to. */
  private readonly invitations = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the reservation being cancelled, to disable its button meanwhile. */
  protected readonly cancellingId = signal<string | null>(null);
  /** Id of the booking in progress whose ways to run longer are open (ADR-0033). */
  protected readonly extendingId = signal<string | null>(null);

  /** Each value starts a load; true shows the loading state, false refreshes quietly. */
  private readonly loads = new Subject<boolean>();
  private readonly liveEvents = new EventBuffer<Reservation>();

  // Re-evaluated every minute, so a reservation moves to Past once it ends.
  private readonly now = signal(Date.now());

  private readonly items = computed(() => {
    const now = this.now();
    return this.reservations().map((reservation) => this.toItem(reservation, now, false));
  });

  private readonly grouped = computed(() => {
    const now = this.now();
    const confirmed = this.items().filter((i) => i.reservation.status === 'Confirmed');
    return {
      // Soonest first: what's next matters most.
      upcoming: confirmed.filter((i) => i.end.getTime() > now).sort(byStart),
      invited: this.invitations()
        .filter((r) => r.status === 'Confirmed' && new Date(r.endDateTime).getTime() > now)
        .map((reservation) => this.toItem(reservation, now, true))
        .sort(byStart),
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
    return {
      upcoming: g.upcoming.length,
      invited: g.invited.length,
      past: g.past.length,
      cancelled: g.cancelled.length,
    };
  });

  constructor() {
    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));

    // switchMap: only the newest load counts, e.g. a reconnect during a refresh.
    this.loads
      .pipe(
        tap((showLoading) => {
          if (showLoading) {
            this.loading.set(true);
          }
          this.liveEvents.start();
        }),
        switchMap((showLoading) =>
          forkJoin({
            mine: this.reservationService.getMine(),
            invited: this.reservationService.getInvited(),
          }).pipe(
            map((lists) => ({ lists, showLoading })),
            catchError(() => of({ lists: null, showLoading })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ lists, showLoading }) => {
        this.loading.set(false);
        const missed = this.liveEvents.drain();
        if (lists) {
          this.reservations.set(lists.mine);
          this.invitations.set(lists.invited);
          this.loadFailed.set(false);
          this.now.set(Date.now());
          // The result may predate changes that arrived while it loaded.
          missed.forEach((reservation) => this.apply(reservation));
        } else if (showLoading) {
          this.loadFailed.set(true); // a failed quiet refresh keeps the list on screen
        }
      });

    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed())
      .subscribe((reservation) => {
        if (this.apply(reservation)) {
          this.liveEvents.record(reservation);
        }
      });

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
    const resource = reservation.resourceName;
    const when = this.when(item);
    if (
      !confirm(
        $localize`:@@reservationForm.confirmCancel:Cancel your ${resource}:resource: reservation for ${when}:when:?`,
      )
    ) {
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
            $localize`:@@reservationForm.cancelled:Your ${cancelled.resourceName}:resource: reservation for ${when}:when: was cancelled.`,
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

  /** Opens or closes the ways to make a booking in progress run longer. */
  protected toggleExtend(item: Item): void {
    this.extendingId.update((id) => (id === item.reservation.id ? null : item.reservation.id));
  }

  protected onExtended(booking: Reservation): void {
    this.extendingId.set(null);
    this.upsert(booking);
  }

  /** An answer to an invitation was sent: the meeting is as the API now has it. */
  protected onAnswered(meeting: Reservation): void {
    this.apply(meeting);
  }

  /**
   * Puts a reservation as it is now where it belongs: the user's own in their list, and a
   * meeting they're invited to in the invitations - or out of them, once it's cancelled or
   * they were taken off it. False when it's neither, e.g. someone else's booking.
   */
  private apply(reservation: Reservation): boolean {
    const userId = this.authService.currentUser()?.id;
    if (reservation.userId === userId) {
      this.upsert(reservation);
      return true;
    }

    const invited =
      reservation.status === 'Confirmed' && reservation.attendees.some((a) => a.id === userId);
    const listed = this.invitations().some((r) => r.id === reservation.id);
    if (invited) {
      this.invitations.update((list) => [
        ...list.filter((r) => r.id !== reservation.id),
        reservation,
      ]);
    } else if (listed) {
      this.invitations.update((list) => list.filter((r) => r.id !== reservation.id));
    }
    return invited || listed;
  }

  private toItem(reservation: Reservation, now: number, invited: boolean): Item {
    const start = new Date(reservation.startDateTime);
    const end = new Date(reservation.endDateTime);
    const minutes = reservation.reminderMinutes;
    const confirmed = reservation.status === 'Confirmed';
    const { opensAt, closesAt } = checkInWindow(reservation);
    const time = (date: Date | string) => formatDate(date, 'HH:mm', this.locale);
    return {
      reservation,
      start,
      end,
      sameDay: start.toDateString() === end.toDateString(),
      duration: formatDuration(start, end),
      inProgress: confirmed && start.getTime() <= now && end.getTime() > now,
      // A reminder goes to the owner only.
      reminder:
        !invited && minutes != null && confirmed && start.getTime() > now
          ? $localize`:@@mine.reminder:Reminder ${minutes}:minutes: min before`
          : null,
      // Anyone invited may check in too (ADR-0032).
      checkInHint:
        confirmed &&
        reservation.checkInRequired &&
        !reservation.checkedInAt &&
        closesAt.getTime() > now
          ? $localize`:@@mine.checkInHint:Check in at the door ${time(opensAt)}:from:-${time(closesAt)}:until:`
          : null,
      checkedIn: confirmed && reservation.checkedInAt !== null,
      releasedNote: reservation.releasedAt
        ? $localize`:@@mine.releasedNote:Nobody checked in by ${time(reservation.releasedAt)}:time:`
        : null,
      attendees: invited ? null : this.attendeeNames(reservation),
      organizer: invited ? this.organizer(reservation) : null,
    };
  }

  private organizer(reservation: Reservation): string {
    const name = reservation.userDisplayName ?? $localize`:@@common.someone:Someone`;
    return $localize`:@@mine.organizer:Organized by ${name}:name:`;
  }

  /**
   * Who's invited, by their answer: "Coming: Ayşe Yılmaz · Can't come: Mehmet Demir · No
   * answer yet: Can Öztürk", the first few of each "and 3 more".
   */
  private attendeeNames(reservation: Reservation): string | null {
    const groups: [AttendeeResponse, string][] = [
      ['Accepted', $localize`:@@mine.coming:Coming`],
      ['Declined', $localize`:@@mine.notComing:Can't come`],
      ['Pending', $localize`:@@mine.noAnswer:No answer yet`],
    ];
    const lines = groups
      .map(([response, label]) => {
        const names = reservation.attendees
          .filter((a) => a.response === response)
          .map((a) => a.name ?? $localize`:@@common.unknownUser:Unknown user`);
        return names.length > 0 ? `${label}: ${shorten(names)}` : null;
      })
      .filter((line) => line !== null);
    return lines.length > 0 ? lines.join(' · ') : null;
  }

  /** "Mon 5 Oct, 10:00-11:30", or with both dates when it spans days. */
  private when(item: Item): string {
    const start = formatDate(item.start, 'EEE d MMM, HH:mm', this.locale);
    const end = formatDate(item.end, item.sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
    return item.sameDay
      ? `${start}-${end}`
      : $localize`:@@common.rangeTo:${start}:start: to ${end}:end:`;
  }

  /**
   * @param showLoading false to refresh quietly, keeping the current list on screen until
   * the new one arrives.
   */
  private load(showLoading: boolean): void {
    this.loads.next(showLoading);
  }

  private upsert(reservation: Reservation): void {
    this.reservations.update((list) =>
      list.some((r) => r.id === reservation.id)
        ? list.map((r) => (r.id === reservation.id ? reservation : r))
        : [...list, reservation],
    );
  }
}

/** "Ayşe Yılmaz, Mehmet Demir", or the first few "and 3 more". */
function shorten(names: string[]): string {
  const shown = names.slice(0, ATTENDEES_SHOWN).join(', ');
  const more = names.length - ATTENDEES_SHOWN;
  return more > 0 ? $localize`:@@mine.andMore:${shown}:names: and ${more}:count: more` : shown;
}

function byStart(a: Item, b: Item): number {
  return a.start.getTime() - b.start.getTime();
}
