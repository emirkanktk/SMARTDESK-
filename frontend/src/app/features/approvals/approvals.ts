import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, merge } from 'rxjs';
import { meetingWhen } from '../../core/i18n/inbox-texts';
import { resourceTypeLabel } from '../../core/i18n/labels';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { ApprovalService } from '../../core/services/approval.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';

/** The most a reason to decline may be, as the API takes it. */
export const REASON_MAX_LENGTH = 500;

/**
 * What waits for an Admin's approval (ADR-0049), at `/admin/approvals`: each request with who
 * asked, when it's for and why, to approve or - saying why - decline. Live: a request made,
 * changed, cancelled or decided by another Admin shows up, or goes, without a reload.
 */
@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-approvals',
  styleUrl: './approvals.scss',
  templateUrl: './approvals.html',
})
export class Approvals {
  private readonly approvals = inject(ApprovalService);
  private readonly hub = inject(ReservationHubService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly typeLabel = resourceTypeLabel;
  protected readonly reasonMaxLength = REASON_MAX_LENGTH;

  private readonly waiting = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** The request being approved or declined, to keep its buttons off meanwhile. */
  protected readonly busyId = signal<string | null>(null);
  /** The request whose reason to decline is being written. */
  protected readonly decliningId = signal<string | null>(null);
  protected reason = '';

  protected readonly items = computed(() =>
    [...this.waiting()]
      .sort((a, b) => a.startDateTime.localeCompare(b.startDateTime))
      .map((request) => ({
        request,
        who: request.userDisplayName ?? $localize`:@@common.someone:Someone`,
        when: meetingWhen(request, this.locale),
        asked: request.requestedAt
          ? $localize`:@@approvals.askedAt:asked ${formatDate(request.requestedAt, 'EEE d MMM, HH:mm', this.locale)}:when:`
          : null,
      })),
  );

  constructor() {
    this.load(true);
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed())
      .subscribe((reservation) => this.apply(reservation));
    this.hub.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => this.load(false));
  }

  protected load(showLoading = true): void {
    this.loading.set(showLoading);
    this.loadFailed.set(false);
    this.approvals
      .getWaiting(true)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (waiting) => this.waiting.set(waiting),
        error: () => this.loadFailed.set(true),
      });
  }

  protected approve(request: Reservation): void {
    this.busyId.set(request.id);
    this.approvals
      .approve(request.id)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (approved) => {
          this.apply(approved);
          this.notifications.success(
            $localize`:@@approvals.approved:Approved: ${approved.resourceName}:resource: for ${this.whoAsked(approved)}:who:, ${meetingWhen(approved, this.locale)}:when:.`,
          );
        },
        error: (error: unknown) => this.refuse(error),
      });
  }

  protected startDeclining(request: Reservation): void {
    this.decliningId.set(request.id);
    this.reason = '';
  }

  protected stopDeclining(): void {
    this.decliningId.set(null);
  }

  protected decline(request: Reservation): void {
    const reason = this.reason.trim();
    if (!reason) {
      return;
    }

    this.busyId.set(request.id);
    this.approvals
      .decline(request.id, reason)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (declined) => {
          this.decliningId.set(null);
          this.apply(declined);
          this.notifications.success(
            $localize`:@@approvals.declined:Declined: ${declined.resourceName}:resource: for ${this.whoAsked(declined)}:who:. They're told why.`,
          );
        },
        error: (error: unknown) => this.refuse(error),
      });
  }

  /** A request as it is now: listed while it waits, gone once it doesn't. */
  private apply(reservation: Reservation): void {
    this.waiting.update((list) => [
      ...list.filter((r) => r.id !== reservation.id),
      ...(reservation.status === 'Pending' ? [reservation] : []),
    ]);
  }

  /** 409: decided by another Admin, cancelled, or lapsed meanwhile. The list as it is now. */
  private refuse(error: unknown): void {
    if (error instanceof ApiError && error.status === 409) {
      this.notifications.error(error.detail ?? error.title);
    }
    this.load(false);
  }

  private whoAsked(reservation: Reservation): string {
    return reservation.userDisplayName ?? $localize`:@@common.someone:Someone`;
  }
}
