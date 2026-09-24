import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class ReservationHubService {
  private readonly authService = inject(AuthService);
  private connection: signalR.HubConnection | null = null;

  connect(): signalR.HubConnection {
    if (this.connection) {
      return this.connection;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl.replace(/\/api$/, '')}/hubs/reservations`, {
        accessTokenFactory: () => this.authService.getToken() ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    this.connection.start().catch((error) => console.error('SignalR connection failed', error));

    return this.connection;
  }

  disconnect(): void {
    this.connection?.stop();
    this.connection = null;
  }
}
