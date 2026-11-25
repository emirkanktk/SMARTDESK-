import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import * as signalR from '@microsoft/signalr';
import { of } from 'rxjs';
import { InboxMessage } from '../models/inbox.model';
import { IssueReport } from '../models/issue.model';
import { Reservation } from '../models/reservation.model';
import { fakeIssue } from '../../../testing/issue-fixtures';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';
import { ReservationHubService } from './reservation-hub.service';

describe('ReservationHubService', () => {
  // The real builder runs, but build() hands back a connection that records its handlers, so a
  // test can play the part of the server. Spying on the prototype, rather than mocking the
  // module, can't be defeated by another test file having loaded the real module first.
  const handlers = new Map<string, (payload: Reservation | InboxMessage | IssueReport) => void>();
  let closed: (() => void) | null = null;
  const connection = {
    on: (event: string, handler: (payload: Reservation | InboxMessage | IssueReport) => void) =>
      handlers.set(event, handler),
    onreconnected: vi.fn(),
    onclose: (handler: () => void) => (closed = handler),
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
  };
  let hub: ReservationHubService;
  let authenticated: WritableSignal<boolean>;
  let renew: ReturnType<typeof vi.fn>;
  let logout: ReturnType<typeof vi.fn>;
  let notifications: { info: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    handlers.clear();
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockReturnValue(
      connection as unknown as signalR.HubConnection,
    );
    authenticated = signal(true);
    // A renewal that finds the session over signs the tab out, as AuthService's does.
    renew = vi.fn(() => {
      authenticated.set(false);
      return of(null);
    });
    logout = vi.fn();
    notifications = { info: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { getToken: () => 'token', isAuthenticated: authenticated, renew, logout },
        },
        { provide: NotificationService, useValue: notifications },
      ],
    });
    hub = TestBed.inject(ReservationHubService);
    hub.connect();
  });

  afterEach(() => {
    hub.disconnect();
    vi.restoreAllMocks();
  });

  it('turns each server event into its own stream', () => {
    const seen: string[] = [];
    hub.created$.subscribe((r) => seen.push(`created ${r.id}`));
    hub.updated$.subscribe((r) => seen.push(`updated ${r.id}`));
    hub.cancelled$.subscribe((r) => seen.push(`cancelled ${r.id}`));
    hub.startingSoon$.subscribe((r) => seen.push(`starting soon ${r.id}`));
    hub.released$.subscribe((r) => seen.push(`released ${r.id}`));
    hub.endingSoon$.subscribe((r) => seen.push(`ending soon ${r.id}`));
    hub.releaseWarning$.subscribe((r) => seen.push(`release warning ${r.id}`));
    hub.inboxMessage$.subscribe((m) => seen.push(`message ${m.id} ${m.kind}`));
    hub.issueReported$.subscribe((i) => seen.push(`issue reported ${i.id}`));
    hub.issueUpdated$.subscribe((i) => seen.push(`issue updated ${i.id}`));

    handlers.get('ReservationCreated')!(fakeReservation({ id: 'a' }));
    handlers.get('ReservationUpdated')!(fakeReservation({ id: 'b' }));
    handlers.get('ReservationCancelled')!(fakeReservation({ id: 'c' }));
    handlers.get('ReservationStartingSoon')!(fakeReservation({ id: 'd' }));
    handlers.get('ReservationReleased')!(fakeReservation({ id: 'e' }));
    handlers.get('ReservationEndingSoon')!(fakeReservation({ id: 'g' }));
    handlers.get('ReservationReleaseWarning')!(fakeReservation({ id: 'h' }));
    handlers.get('InboxMessageReceived')!({ id: 'f', kind: 'MeetingInvitation' } as InboxMessage);
    handlers.get('IssueReported')!(fakeIssue({ id: 'i' }));
    handlers.get('IssueUpdated')!(fakeIssue({ id: 'j' }));

    expect(seen).toEqual([
      'created a',
      'updated b',
      'cancelled c',
      'starting soon d',
      'released e',
      'ending soon g',
      'release warning h',
      'message f MeetingInvitation',
      'issue reported i',
      'issue updated j',
    ]);
  });

  describe('catching up', () => {
    let catchUps: number;

    /** Starts over, with the server refusing the first `failures` attempts to connect. */
    function reconnectWithFailures(failures: number): void {
      hub.disconnect();
      connection.start.mockReset();
      for (let i = 0; i < failures; i++) {
        connection.start.mockImplementationOnce(() => Promise.reject(new Error('offline')));
      }
      connection.start.mockImplementation(() => Promise.resolve());
      catchUps = 0;
      hub.reconnected$.subscribe(() => catchUps++);
      hub.connect();
    }

    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('asks screens to catch up when the connection only started on a retry', async () => {
      reconnectWithFailures(2);
      await vi.advanceTimersByTimeAsync(2_000); // retries after 0 and 2 s

      expect(connection.start).toHaveBeenCalledTimes(3);
      expect(catchUps).toBe(1);
    });

    it('does not when the first attempt works', async () => {
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);

      expect(connection.start).toHaveBeenCalledTimes(1);
      expect(catchUps).toBe(0);
    });

    it('starts again when the server ends the connection, and asks screens to catch up', async () => {
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);

      closed!(); // e.g. the user signed in on another device
      await vi.advanceTimersByTimeAsync(0);

      expect(connection.start).toHaveBeenCalledTimes(2);
      expect(catchUps).toBe(1);
    });

    it('keeps trying when the server is not there to start again', async () => {
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);
      connection.start.mockImplementationOnce(() => Promise.reject(new Error('offline')));

      closed!();
      await vi.advanceTimersByTimeAsync(2_000); // tried at once, then after 2 s

      expect(connection.start).toHaveBeenCalledTimes(3);
      expect(catchUps).toBe(1);
    });

    it('does not start again once signed out', async () => {
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);
      hub.disconnect();

      closed!();
      await vi.advanceTimersByTimeAsync(30_000);

      expect(connection.start).toHaveBeenCalledTimes(1);
    });

    it('goes to sign in at once when the server refuses it and the session is over', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);
      // As SignalR reports a refused negotiation: an error of its own, the status in its text.
      connection.start.mockImplementationOnce(() =>
        Promise.reject(
          new Error(
            "Failed to complete negotiation with the server: Error: Unauthorized: Status code '401'",
          ),
        ),
      );

      closed!(); // this device was signed out from another one (ADR-0061)
      await vi.advanceTimersByTimeAsync(0);

      expect(renew).toHaveBeenCalledTimes(1);
      expect(logout).toHaveBeenCalled();
      expect(notifications.info).toHaveBeenCalledWith(
        'Your session has expired. Please sign in again.',
      );
      expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
    });

    it('carries on when the session goes on: a new token for the next attempt', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      renew.mockImplementation(() => of('renewed-token'));
      reconnectWithFailures(0);
      await vi.advanceTimersByTimeAsync(0);
      connection.start.mockImplementationOnce(() =>
        Promise.reject(new signalR.HttpError('Unauthorized', 401)),
      );

      closed!(); // e.g. the user's role changed, so the old token's roles no longer hold
      await vi.advanceTimersByTimeAsync(2_000);

      expect(renew).toHaveBeenCalledTimes(1);
      expect(logout).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
      expect(connection.start).toHaveBeenCalledTimes(3);
    });

    it('does not ask when the server is only out of reach', async () => {
      reconnectWithFailures(2);
      await vi.advanceTimersByTimeAsync(2_000);

      expect(renew).not.toHaveBeenCalled();
    });

    it('does not after a sign-out while it was still retrying', async () => {
      reconnectWithFailures(1);
      await Promise.resolve(); // the first attempt fails
      hub.disconnect();
      await vi.advanceTimersByTimeAsync(30_000);

      expect(connection.start).toHaveBeenCalledTimes(1);
      expect(catchUps).toBe(0);
    });
  });

  it('returns nothing to the server from a handler', () => {
    // SignalR treats a returned value as an answer the server didn't ask for and logs an error.
    expect(handlers.size).toBe(10);
    for (const handler of handlers.values()) {
      expect(handler(fakeReservation())).toBeUndefined();
    }
  });
});
