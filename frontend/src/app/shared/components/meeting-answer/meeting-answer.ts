import { Component, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import {
  AttendeeResponse,
  DECLINE_REASON_MAX_LENGTH,
  Reservation,
} from '../../../core/models/reservation.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationService } from '../../../core/services/reservation.service';

let nextId = 1;

/**
 * The signed-in user's answer to a meeting they're invited to (ADR-0032): coming, or not and
 * why, for the organizer only. Shows what they answered, and lets them change it. Shows
 * nothing for someone who isn't on the meeting's list. With `following`, the answer is for this
 * date of a meeting that repeats and every later one (ADR-0035).
 */
@Component({
  selector: 'app-meeting-answer',
  templateUrl: './meeting-answer.html',
  styleUrl: './meeting-answer.scss',
})
export class MeetingAnswer {
  private readonly reservationService = inject(ReservationService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly reservation = input.required<Reservation>();
  /** Answer for this date and every later one of its series. */
  readonly following = input(false);
  /** The meeting as it is after the answer. */
  readonly answered = output<Reservation>();

  protected readonly maxLength = DECLINE_REASON_MAX_LENGTH;
  protected readonly reasonId = `decline-reason-${nextId++}`;

  /** What the user answered so far; null when they aren't on the list. */
  protected readonly response = computed<AttendeeResponse | null>(() => {
    const userId = this.auth.currentUser()?.id;
    return this.reservation().attendees.find((a) => a.id === userId)?.response ?? null;
  });
  /** Answering again after having answered: the choices are shown again. */
  protected readonly changing = signal(false);
  /** The form for saying why they can't come is open. */
  protected readonly declining = signal(false);
  protected readonly reason = signal('');
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);

  protected accept(): void {
    this.send('Accepted');
  }

  protected startDeclining(): void {
    this.declining.set(true);
    this.error.set(null);
  }

  protected decline(event: Event): void {
    event.preventDefault();
    if (!this.reason().trim()) {
      this.error.set($localize`:@@answer.reasonRequired:Say why you can't come.`);
      return;
    }
    this.send('Declined', this.reason().trim());
  }

  protected back(): void {
    this.declining.set(false);
    this.changing.set(false);
    this.error.set(null);
  }

  protected onReason(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  private send(response: 'Accepted' | 'Declined', reason?: string): void {
    this.sending.set(true);
    this.error.set(null);
    this.reservationService
      .respond(this.reservation().id, {
        response,
        ...(reason === undefined ? {} : { reason }),
        ...(this.following() ? { following: true } : {}),
      })
      .pipe(finalize(() => this.sending.set(false)))
      .subscribe({
        next: (meeting) => {
          this.declining.set(false);
          this.changing.set(false);
          this.reason.set('');
          const organizer = meeting.userDisplayName;
          this.notifications.success(
            organizer
              ? $localize`:@@answer.sentTo:Your answer was sent to ${organizer}:name:.`
              : $localize`:@@answer.sent:Your answer was sent.`,
          );
          this.answered.emit(meeting);
        },
        error: (error: unknown) =>
          this.error.set(
            error instanceof ApiError && error.status >= 400 && error.status < 500
              ? (error.fieldErrors['Reason']?.[0] ?? error.detail ?? error.title)
              : $localize`:@@answer.failed:Your answer could not be sent. Try again.`,
          ),
      });
  }
}
