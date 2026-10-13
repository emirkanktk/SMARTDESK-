import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, filter, finalize, merge, of, startWith, switchMap } from 'rxjs';
import { WaitlistEntry } from '../../../core/models/waitlist.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationHubService } from '../../../core/services/reservation-hub.service';
import { WaitlistService } from '../../../core/services/waitlist.service';

/**
 * What the user is waiting for (ADR-0043), on My reservations: each time, their place in line,
 * and a way to leave it. Nothing when they wait for nothing. A time booked for them comes in as
 * their booking, live, and leaves the list then.
 */
@Component({
  selector: 'app-waiting-list',
  styleUrl: './waiting-list.scss',
  templateUrl: './waiting-list.html',
})
export class WaitingList {
  private readonly waitlist = inject(WaitlistService);
  private readonly hub = inject(ReservationHubService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly entries = signal<WaitlistEntry[]>([]);
  /** Id of the entry being left, to disable its button meanwhile. */
  protected readonly leavingId = signal<string | null>(null);
  private readonly loads = new Subject<void>();

  constructor() {
    // Again when a booking of the user's comes in - one from the line, perhaps - or after a
    // reconnect, which may have missed one.
    const mine = this.hub.created$.pipe(
      filter((reservation) => reservation.userId === this.auth.currentUser()?.id),
    );
    merge(this.loads, mine, this.hub.reconnected$)
      .pipe(
        startWith(undefined),
        switchMap(() => this.waitlist.getMine().pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((entries) => {
        if (entries) {
          this.entries.set(entries);
        }
      });
  }

  /** "Wed 7 Oct, 14:00-15:00", with both dates when it runs past midnight. */
  protected when(entry: WaitlistEntry): string {
    const start = new Date(entry.startDateTime);
    const end = new Date(entry.endDateTime);
    const from = formatDate(start, 'EEE d MMM, HH:mm', this.locale);
    return start.toDateString() === end.toDateString()
      ? `${from}-${formatDate(end, 'HH:mm', this.locale)}`
      : `${from} - ${formatDate(end, 'EEE d MMM, HH:mm', this.locale)}`;
  }

  protected leaveLabel(entry: WaitlistEntry): string {
    return $localize`:@@waitlist.leaveLabel:Leave the waiting list for ${entry.resourceName}:resource:, ${this.when(entry)}:when:`;
  }

  protected leave(entry: WaitlistEntry): void {
    const resource = entry.resourceName;
    const when = this.when(entry);
    if (
      !confirm(
        $localize`:@@waitlist.confirmLeave:Stop waiting for ${resource}:resource:, ${when}:when:?`,
      )
    ) {
      return;
    }

    this.leavingId.set(entry.id);
    this.waitlist
      .leave(entry.id)
      .pipe(finalize(() => this.leavingId.set(null)))
      .subscribe({
        next: () => {
          this.entries.update((list) => list.filter((e) => e.id !== entry.id));
          this.notifications.success(
            $localize`:@@waitlist.left:You're no longer waiting for ${resource}:resource:, ${when}:when:.`,
          );
        },
        // Gone meanwhile - booked for them, or left elsewhere: the list as it is now.
        error: () => this.loads.next(),
      });
  }
}
