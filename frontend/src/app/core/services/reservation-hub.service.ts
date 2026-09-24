import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

// The reservation object from the hub, the same shape as the HTTP reservation body.
export interface ReservationChanged {
  id: string;
  resourceId: string;
  resourceName: string;
  resourceTypeName: string;
  resourceLocation: string;
  userId: string;
  startDateTime: string;
  endDateTime: string;
  status: string;
  note: string | null;
  userDisplayName: string | null;
}

// First waits, then every further retry stays at 30s. Never returns null, so a drop
// does not end the connection for good.
const retryDelaysMs = [0, 2000, 5000, 10000];

function nextDelay(attempt: number): number {
  return retryDelaysMs[attempt] ?? 30_000;
}

@Injectable({ providedIn: 'root' })
export class ReservationHubService {
  private readonly authService = inject(AuthService);
  private connection: signalR.HubConnection | null = null;
  private startTimer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;

  private readonly created = new Subject<ReservationChanged>();
  private readonly updated = new Subject<ReservationChanged>();
  private readonly cancelled = new Subject<ReservationChanged>();
  private readonly reconnected = new Subject<void>();

  readonly created$: Observable<ReservationChanged> = this.created.asObservable();
  readonly updated$: Observable<ReservationChanged> = this.updated.asObservable();
  readonly cancelled$: Observable<ReservationChanged> = this.cancelled.asObservable();
  readonly reconnected$: Observable<void> = this.reconnected.asObservable();

  connect(): void {
    this.stopped = false;
    if (this.connection) {
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl.replace(/\/api$/, '')}/hubs/reservations`, {
        accessTokenFactory: () => this.authService.getToken() ?? '',
        withCredentials: false,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) => nextDelay(previousRetryCount),
      })
      .build();

    connection.on('ReservationCreated', (reservation: ReservationChanged) => this.created.next(reservation));
    connection.on('ReservationUpdated', (reservation: ReservationChanged) => this.updated.next(reservation));
    connection.on('ReservationCancelled', (reservation: ReservationChanged) => this.cancelled.next(reservation));
    connection.onreconnected(() => this.reconnected.next());

    this.connection = connection;
    this.startWithRetry(connection, 0);
  }

  disconnect(): void {
    this.stopped = true;
    if (this.startTimer !== null) {
      clearTimeout(this.startTimer);
      this.startTimer = null;
    }
    const connection = this.connection;
    this.connection = null;
    void connection?.stop();
  }

  private startWithRetry(connection: signalR.HubConnection, attempt: number): void {
    if (this.stopped || this.connection !== connection) {
      return;
    }

    connection.start().catch(() => {
      if (this.stopped || this.connection !== connection) {
        return;
      }
      this.startTimer = setTimeout(() => this.startWithRetry(connection, attempt + 1), nextDelay(attempt));
    });
  }
}
