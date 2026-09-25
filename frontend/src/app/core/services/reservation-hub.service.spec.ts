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
  const connection = {
    on: (event: string, handler: (payload: Reservation) => void) => handlers.set(event, handler),
    onreconnected: vi.fn(),
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

  it('returns nothing to the server from a handler', () => {
    // SignalR treats a returned value as an answer the server didn't ask for and logs an error.
    expect(handlers.size).toBe(4);
    for (const handler of handlers.values()) {
      expect(handler(fakeReservation())).toBeUndefined();
    }
  });
});
