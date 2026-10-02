import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Reservation } from '../../core/models/reservation.model';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ExtendBooking } from '../../shared/components/extend-booking/extend-booking';
import { Icon } from '../../shared/components/icon/icon';

/**
 * Asks the signed-in user, shortly before one of their bookings ends, whether they need longer
 * (ADR-0033): a card in the corner with buttons that extend it, instead of the edit form and a
 * new end. One at a time; a booking extended or cancelled elsewhere leaves by itself. Also a
 * desktop notification when the browser allows one, so it's seen from another tab too.
 */
@Component({
  selector: 'app-ending-soon',
  imports: [ExtendBooking, Icon],
  templateUrl: './ending-soon.html',
  styleUrl: './ending-soon.scss',
})
export class EndingSoon {
  private readonly hub = inject(ReservationHubService);
  private readonly locale = inject(LOCALE_ID);

  private readonly queue = signal<Reservation[]>([]);
  /** The booking asked about now, as a list of one so its card starts afresh for each. */
  protected readonly current = computed(() => this.queue().slice(0, 1));

  constructor() {
    this.hub.endingSoon$.pipe(takeUntilDestroyed()).subscribe((booking) => this.ask(booking));
    this.hub.updated$.pipe(takeUntilDestroyed()).subscribe((booking) => this.follow(booking));
    this.hub.cancelled$.pipe(takeUntilDestroyed()).subscribe((booking) => this.drop(booking.id));
  }

  protected endTime(booking: Reservation): string {
    return formatDate(booking.endDateTime, 'HH:mm', this.locale);
  }

  protected dismiss(booking: Reservation): void {
    this.drop(booking.id);
  }

  protected onExtended(booking: Reservation): void {
    this.drop(booking.id);
  }

  private ask(booking: Reservation): void {
    if (this.queue().some((b) => b.id === booking.id)) {
      return;
    }
    this.queue.update((list) => [...list, booking]);

    if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      // The tag makes several open tabs show one notification, not one each.
      const desktop = new Notification(
        $localize`:@@endingSoon.desktopTitle:Your booking ends soon`,
        {
          body: $localize`:@@endingSoon.desktopBody:${booking.resourceName}:resource: ends at ${this.endTime(booking)}:time:. Need more time? Extend it in SmartDesk.`,
          tag: `ending-${booking.id}`,
        },
      );
      desktop.onclick = () => {
        window.focus();
        desktop.close();
      };
    }
  }

  /** Extended elsewhere: nothing more to ask. Otherwise kept, with its latest details. */
  private follow(booking: Reservation): void {
    const asked = this.queue().find((b) => b.id === booking.id);
    if (!asked) {
      return;
    }
    if (booking.endDateTime !== asked.endDateTime || booking.status !== 'Confirmed') {
      this.drop(booking.id);
    } else {
      this.queue.update((list) => list.map((b) => (b.id === booking.id ? booking : b)));
    }
  }

  private drop(id: string): void {
    this.queue.update((list) => list.filter((b) => b.id !== id));
  }
}
