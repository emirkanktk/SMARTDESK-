import { formatDate } from '@angular/common';
import { Injectable, LOCALE_ID, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Reservation } from '../models/reservation.model';
import { NotificationService } from './notification.service';
import { ReservationHubService } from './reservation-hub.service';

/** A reminder toast stays up longer than an ordinary one: it's easy to miss otherwise. */
export const REMINDER_TOAST_MS = 15_000;

export type DesktopPermission = NotificationPermission | 'unsupported';

/**
 * Shows the signed-in user's reservation reminders (ADR-0018): a toast in the app, and a
 * desktop notification when the browser allows one, so it's seen from another tab too.
 * Clicking the desktop notification opens the reservation.
 */
@Injectable({ providedIn: 'root' })
export class ReminderAlertsService {
  private readonly hub = inject(ReservationHubService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly locale = inject(LOCALE_ID);
  private started = false;

  /** Called once by the app; later calls do nothing. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    this.hub.startingSoon$.subscribe((reservation) => this.show(reservation));
  }

  permission(): DesktopPermission {
    return typeof Notification === 'undefined' ? 'unsupported' : Notification.permission;
  }

  /**
   * Asks once whether desktop notifications may be shown. Call it from a user action (saving
   * a reservation with a reminder): browsers ignore or block requests that come from nowhere.
   */
  requestPermissionIfUnasked(): void {
    if (this.permission() === 'default') {
      void Notification.requestPermission();
    }
  }

  private show(reservation: Reservation): void {
    const start = new Date(reservation.startDateTime);
    const minutes = Math.round((start.getTime() - Date.now()) / 60_000);
    const at = formatDate(start, 'HH:mm', this.locale);
    const message =
      minutes > 0
        ? $localize`:@@reminder.startsIn:${reservation.resourceName}:resource: starts in ${minutes}:minutes: min, at ${at}:time:.`
        : $localize`:@@reminder.startsNow:${reservation.resourceName}:resource: starts now, at ${at}:time:.`;

    this.notifications.info(message, REMINDER_TOAST_MS);

    if (this.permission() === 'granted') {
      // The tag makes several open tabs show one notification, not one each.
      const desktop = new Notification($localize`:@@reminder.title:Reservation starting soon`, {
        body: message,
        tag: `reminder-${reservation.id}`,
      });
      desktop.onclick = () => {
        window.focus();
        void this.router.navigate(['/reservations', reservation.id, 'edit']);
        desktop.close();
      };
    }
  }
}
