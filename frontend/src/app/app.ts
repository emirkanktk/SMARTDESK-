import { Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
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
    // New problem reports, for those who look after resources (ADR-0037).
    inject(IssueAlertsService).start();

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
