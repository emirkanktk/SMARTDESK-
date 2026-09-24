import { Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/services/auth.service';
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
    effect(() => {
      if (this.authService.isAuthenticated()) {
        this.reservationHub.connect();
      } else {
        this.reservationHub.disconnect();
      }
    });
  }
}
