import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, OnInit, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { Reservation } from '../../../core/models/reservation.model';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationService } from '../../../core/services/reservation.service';

/**
 * Buttons that let a booking run longer with a click (ADR-0033): "+15 min", "+30 min",
 * "+1 h", only those the resource is still free for. Says so when it can't run any longer.
 */
@Component({
  selector: 'app-extend-booking',
  templateUrl: './extend-booking.html',
  styleUrl: './extend-booking.scss',
})
export class ExtendBooking implements OnInit {
  private readonly reservationService = inject(ReservationService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);

  readonly reservation = input.required<Reservation>();
  /** The booking as it is after running longer. */
  readonly extended = output<Reservation>();

  /** The minutes it can still run longer by; null while they load. */
  protected readonly options = signal<number[] | null>(null);
  protected readonly extending = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.reservationService.getExtensionOptions(this.reservation().id).subscribe({
      next: ({ minutes }) => this.options.set(minutes),
      error: (error: unknown) => {
        this.options.set([]);
        this.error.set(this.reason(error));
      },
    });
  }

  /** "+15 min", or "+1 h" for a whole hour. */
  protected label(minutes: number): string {
    return minutes % 60 === 0
      ? $localize`:@@extend.hours:+${minutes / 60}:hours: h`
      : $localize`:@@extend.minutes:+${minutes}:minutes: min`;
  }

  protected extend(minutes: number): void {
    this.extending.set(minutes);
    this.error.set(null);
    this.reservationService
      .extend(this.reservation().id, minutes)
      .pipe(finalize(() => this.extending.set(null)))
      .subscribe({
        next: (booking) => {
          const until = formatDate(booking.endDateTime, 'HH:mm', this.locale);
          this.notifications.success(
            $localize`:@@extend.done:${booking.resourceName}:resource: is now booked until ${until}:time:.`,
          );
          this.extended.emit(booking);
        },
        error: (error: unknown) => this.error.set(this.reason(error)),
      });
  }

  private reason(error: unknown): string {
    return error instanceof ApiError && error.status >= 400 && error.status < 500
      ? (error.detail ?? error.title)
      : $localize`:@@extend.failed:It could not be extended. Try again.`;
  }
}
