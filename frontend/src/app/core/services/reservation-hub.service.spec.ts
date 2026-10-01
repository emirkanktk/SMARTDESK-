import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';
import { Reservation } from '../models/reservation.model';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { AuthService } from './auth.service';
import { ReservationHubService } from './reservation-hub.service';

describe('ReservationHubService', () => {
  // The real builder runs, but build() hands back a connection that records its handlers, so a
  // test can play the part of the server. Spying on the prototype, rather than mocking the
  // module, can't be defeated by another test file having loaded the real module first.
  const handlers = new Map<string, (payload: Reservation) => void>();
  let closed: (() => void) | null = null;
  const connection = {
    on: (event: string, handler: (payload: Reservation) => void) => handlers.set(event, handler),
    onreconnected: vi.fn(),
    onclose: (handler: () => void) => (closed = handler),
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
  };
  let hub: ReservationHubService;

  beforeEach(() => {
    handlers.clear();
    vi.spyOn(signalR.HubConnectionBuilder.prototype, 'build').mockReturnValue(
      connection as unknown as signalR.HubConnection,
    );
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthService,
          useValue: { getToken: () => 'token', isAuthenticated: signal(true) },
        },
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

    handlers.get('ReservationCreated')!(fakeReservation({ id: 'a' }));
    handlers.get('ReservationUpdated')!(fakeReservation({ id: 'b' }));
    handlers.get('ReservationCancelled')!(fakeReservation({ id: 'c' }));
    handlers.get('ReservationStartingSoon')!(fakeReservation({ id: 'd' }));

    expect(seen).toEqual(['created a', 'updated b', 'cancelled c', 'starting soon d']);
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
    expect(handlers.size).toBe(4);
    for (const handler of handlers.values()) {
      expect(handler(fakeReservation())).toBeUndefined();
    }
  });
});
