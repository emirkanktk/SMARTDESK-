import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { InboxMessage } from '../models/inbox.model';
import { Reservation } from '../models/reservation.model';
import { AuthService } from './auth.service';

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

  private readonly created = new Subject<Reservation>();
  private readonly updated = new Subject<Reservation>();
  private readonly cancelled = new Subject<Reservation>();
  private readonly startingSoon = new Subject<Reservation>();
  private readonly released = new Subject<Reservation>();
  private readonly endingSoon = new Subject<Reservation>();
  private readonly releaseWarning = new Subject<Reservation>();
  private readonly inboxMessage = new Subject<InboxMessage>();
  private readonly reconnected = new Subject<void>();

  readonly created$: Observable<Reservation> = this.created.asObservable();
  readonly updated$: Observable<Reservation> = this.updated.asObservable();
  readonly cancelled$: Observable<Reservation> = this.cancelled.asObservable();
  /** The signed-in user's own reminders; the API sends them to the owner only. */
  readonly startingSoon$: Observable<Reservation> = this.startingSoon.asObservable();
  /**
   * The signed-in user's own bookings released because nobody checked in (ADR-0030), to the
   * owner only. Everyone, the owner included, also gets the booking in `cancelled$`.
   */
  readonly released$: Observable<Reservation> = this.released.asObservable();
  /**
   * The signed-in user's own bookings that are about to end, asking whether they need longer
   * (ADR-0033); to the owner only.
   */
  readonly endingSoon$: Observable<Reservation> = this.endingSoon.asObservable();
  /**
   * Nobody has checked in to a booking the user owns or is invited to, and it's released in
   * five minutes (ADR-0034); to them only.
   */
  readonly releaseWarning$: Observable<Reservation> = this.releaseWarning.asObservable();
  /** Messages to the signed-in user's inbox (ADR-0032); the API sends each to its recipient only. */
  readonly inboxMessage$: Observable<InboxMessage> = this.inboxMessage.asObservable();
  readonly reconnected$: Observable<void> = this.reconnected.asObservable();

  connect(): void {
    this.stopped = false;
    if (this.connection) {
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl.replace(/\/api$/, '')}/hubs/reservations`, {
        // A fresh token for every (re)connect, renewed first if it's about to expire (ADR-0026).
        accessTokenFactory: async () => (await firstValueFrom(this.authService.validToken())) ?? '',
        withCredentials: false,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) => nextDelay(previousRetryCount),
      })
      .build();

    // Each event carries the reservation as it is after the change, same shape as HTTP.
    connection.on('ReservationCreated', (reservation: Reservation) =>
      this.created.next(reservation),
    );
    connection.on('ReservationUpdated', (reservation: Reservation) =>
      this.updated.next(reservation),
    );
    connection.on('ReservationCancelled', (reservation: Reservation) =>
      this.cancelled.next(reservation),
    );
    connection.on('ReservationStartingSoon', (reservation: Reservation) =>
      this.startingSoon.next(reservation),
    );
    connection.on('ReservationReleased', (reservation: Reservation) =>
      this.released.next(reservation),
    );
    connection.on('ReservationEndingSoon', (reservation: Reservation) =>
      this.endingSoon.next(reservation),
    );
    connection.on('ReservationReleaseWarning', (reservation: Reservation) =>
      this.releaseWarning.next(reservation),
    );
    connection.on('InboxMessageReceived', (message: InboxMessage) =>
      this.inboxMessage.next(message),
    );
    connection.onreconnected(() => this.reconnected.next());
    // The server ends a user's connections when their account or sessions change - a sign-in
    // on another device does (ADR-0021, ADR-0026) - for the app to come back through the token
    // checks. SignalR doesn't reconnect after a close the server asked for, so start again
    // while still signed in; screens catch up once it's back.
    connection.onclose(() => {
      if (!this.stopped && this.connection === connection) {
        this.startWithRetry(connection, 1);
      }
    });

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

    connection.start().then(
      () => {
        // Screens loaded over HTTP while the first attempts failed, and missed whatever
        // changed meanwhile: the same catch-up as after a dropped connection comes back.
        if (attempt > 0 && !this.stopped && this.connection === connection) {
          this.reconnected.next();
        }
      },
      () => {
        if (this.stopped || this.connection !== connection) {
          return;
        }
        this.startTimer = setTimeout(
          () => this.startWithRetry(connection, attempt + 1),
          nextDelay(attempt),
        );
      },
    );
  }
}
