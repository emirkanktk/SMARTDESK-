import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Reservation } from '../models/reservation.model';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { AuthService } from './auth.service';
import { ReservationHubService } from './reservation-hub.service';

// The connection is replaced with one that records its handlers, so a test can play the
// part of the server and "send" an event.
const handlers = new Map<string, (payload: Reservation) => void>();
const connection = {
  on: vi.fn((event: string, handler: (payload: Reservation) => void) =>
    handlers.set(event, handler),
  ),
  onreconnected: vi.fn(),
  start: vi.fn(() => Promise.resolve()),
  stop: vi.fn(() => Promise.resolve()),
};

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    build() {
      return connection;
    }
  },
}));

describe('ReservationHubService', () => {
  let hub: ReservationHubService;

  beforeEach(() => {
    handlers.clear();
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

  afterEach(() => hub.disconnect());

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
    for (const handler of handlers.values()) {
      expect(handler(fakeReservation())).toBeUndefined();
    }
  });
});
