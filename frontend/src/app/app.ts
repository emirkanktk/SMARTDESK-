import { Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppBadgeService } from './core/services/app-badge.service';
import { AppUpdateService } from './core/services/app-update.service';
import { AuthService } from './core/services/auth.service';
import { InboxService } from './core/services/inbox.service';
import { IssueAlertsService } from './core/services/issue-alerts.service';
import { ReminderAlertsService } from './core/services/reminder-alerts.service';
import { ReservationHubService } from './core/services/reservation-hub.service';
import { NotificationHost } from './layout/notification-host/notification-host';

@Component({
  imports: [RouterOutlet, NotificationHost],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly authService = inject(AuthService);
  private readonly reservationHub = inject(ReservationHubService);

  constructor() {
    // Reminders only arrive while connected, i.e. signed in; so do inbox messages (ADR-0032).
    inject(ReminderAlertsService).start();
    inject(InboxService).start();
    // Its unread count on the installed app's icon (ADR-0058).
    inject(AppBadgeService).start();
    // New problem reports, for those who look after resources (ADR-0037).
    inject(IssueAlertsService).start();
    // A new version of the installed app, once its service worker has it (ADR-0054).
    inject(AppUpdateService).start();

    // Not while a temporary password must be replaced: the API refuses the hub then (ADR-0021),
    // and the connection opens once the new password - and its new token - are in place.
    effect(() => {
      if (this.authService.isAuthenticated() && !this.authService.passwordChangeRequired()) {
        this.reservationHub.connect();
      } else {
        this.reservationHub.disconnect();
      }
    });
  }
}
