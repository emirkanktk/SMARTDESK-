import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Lobby as LobbyData, LobbyRoom } from '../../core/models/lobby.model';
import { LobbyService } from '../../core/services/lobby.service';
import { LOBBY_AGENDA, LOBBY_REFRESH_MS, Lobby } from './lobby';

// Local times, so what the screen prints reads the same in every time zone the tests run in.
const at = (time: string) => new Date(`2026-11-26T${time}`);
const iso = (time: string) => at(time).toISOString();
const TOKEN = 'b'.repeat(64);

const booking = (start: string, end: string) => ({
  startDateTime: iso(start),
  endDateTime: iso(end),
  checkInNeeded: false,
  checkedIn: false,
});

function room(overrides: Partial<LobbyRoom>): LobbyRoom {
  return {
    id: overrides.name ?? 'room',
    name: 'Room',
    location: '2nd Floor',
    capacity: 8,
    status: 'Active',
    bookings: [],
    blocks: [],
    ...overrides,
  };
}

function lobby(overrides: Partial<LobbyData> = {}): LobbyData {
  return {
    screenName: 'Entrance',
    location: null,
    now: iso('10:05'),
    appUrl: 'https://smartdesk.example/',
    peopleToday: 17,
    rooms: [
      room({ name: 'Meeting Room A', bookings: [booking('11:00', '12:00')] }),
      room({
        name: 'Meeting Room B',
        bookings: [booking('10:00', '10:45'), booking('13:00', '14:00')],
      }),
      room({ name: 'Meeting Room C', status: 'Maintenance' }),
      room({
        name: 'Board Room',
        location: '5th Floor',
        capacity: 16,
        blocks: [{ startDateTime: iso('09:00'), endDateTime: iso('12:00'), reason: 'Cleaning' }],
        bookings: [booking('15:00', '16:00')],
      }),
    ],
    desks: [
      { location: '3rd Floor', free: 4, total: 6 },
      { location: '5th Floor', free: 0, total: 8 },
    ],
    ...overrides,
  };
}

describe('Lobby', () => {
  let get: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Lobby>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({
      toFake: ['Date', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'],
    });
    vi.setSystemTime(at('10:05'));
    localStorage.clear();
    window.history.replaceState(null, '', '/lobby');
    get = vi.fn(() => of(lobby()));
    TestBed.configureTestingModule({ providers: [{ provide: LobbyService, useValue: { get } }] });
  });

  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
    localStorage.clear();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Lobby);
    element = fixture.nativeElement;
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
  }

  const text = (node: Element = element) => node.textContent!.replace(/\s+/g, ' ').trim();
  const rooms = () => [...element.querySelectorAll('.room')];
  // The count and its words are boxes of their own, set apart by the gap between them.
  const people = () =>
    [...element.querySelector('.people')!.children].map((child) => text(child)).join(' ');

  it('keeps the token from its link, and takes it out of the address', async () => {
    window.history.replaceState(null, '', `/lobby#${TOKEN}`);
    await render();

    expect(get).toHaveBeenCalledWith(TOKEN, expect.any(String));
    expect(window.location.hash).toBe('');
    expect(localStorage.getItem('smartdesk_lobby')).toBe(TOKEN);
  });

  it('says it is not set up without a token, and asks nothing', async () => {
    await render();

    expect(text()).toContain("This screen isn't set up");
    expect(get).not.toHaveBeenCalled();
  });

  it('shows each room as its door would, how many are free, and the rest of the office', async () => {
    localStorage.setItem('smartdesk_lobby', TOKEN);
    await render();

    expect(text(element.querySelector('h1')!)).toBe('Entrance');
    expect(text(element.querySelector('.where')!)).toBe('The whole office');
    expect(rooms().map((li) => [...li.classList].filter((c) => c !== 'room'))).toEqual([
      ['free'],
      ['busy'],
      ['unavailable'],
      ['unavailable'],
    ]);
    expect(rooms().map((li) => text(li.querySelector('.room-detail')!))).toEqual([
      'until 11:00',
      'until 10:45',
      'Maintenance',
      'Cleaning',
    ]);
    expect(text(rooms()[3].querySelector('.room-where')!)).toBe('5th Floor · 16 people');
    expect(text(element.querySelector('.free-count')!)).toBe('1 free now');
    expect(people()).toBe('17 people in the office today');
    expect(
      [...element.querySelectorAll('.desks li')].map((li) =>
        [...li.children].map((child) => text(child)).join(' '),
      ),
    ).toEqual(['3rd Floor 4 / 6', '5th Floor 0 / 8']);
    expect(element.querySelector('.app app-qr-code svg')).not.toBeNull();
  });

  it('lists the meetings still to come, earliest first, as many as fit', async () => {
    localStorage.setItem('smartdesk_lobby', TOKEN);
    const many = Array.from({ length: LOBBY_AGENDA + 2 }, (_, i) =>
      booking(`1${i}:30`, `1${i}:45`),
    );
    get.mockReturnValue(of(lobby({ rooms: [room({ name: 'Board Room', bookings: many })] })));
    await render();
    expect([...element.querySelectorAll('.agenda li')]).toHaveLength(LOBBY_AGENDA);

    fixture.destroy();
    get.mockReturnValue(of(lobby()));
    await render();
    expect(
      [...element.querySelectorAll('.agenda li')].map((li) =>
        [...li.children].map((child) => text(child)).join(' '),
      ),
    ).toEqual([
      '11:00–12:00 Meeting Room A 2nd Floor',
      '13:00–14:00 Meeting Room B 2nd Floor',
      '15:00–16:00 Board Room 5th Floor',
    ]);
  });

  it('asks again every half minute, and keeps showing what it knew when it cannot', async () => {
    localStorage.setItem('smartdesk_lobby', TOKEN);
    await render();
    expect(get).toHaveBeenCalledTimes(1);

    get.mockReturnValue(throwError(() => new ApiError(0, 'Offline')));
    vi.advanceTimersByTime(LOBBY_REFRESH_MS);
    fixture.detectChanges();
    expect(get).toHaveBeenCalledTimes(2);
    expect(text()).toContain("Can't reach SmartDesk right now; trying again.");
    expect(rooms()).toHaveLength(4);

    get.mockReturnValue(of(lobby({ peopleToday: 1 })));
    vi.advanceTimersByTime(LOBBY_REFRESH_MS);
    fixture.detectChanges();
    expect(text()).not.toContain("Can't reach SmartDesk");
    expect(people()).toBe('1 person in the office today');
  });

  it('forgets a token whose screen was taken down, and says so', async () => {
    localStorage.setItem('smartdesk_lobby', TOKEN);
    get.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(text()).toContain("This screen isn't set up");
    expect(localStorage.getItem('smartdesk_lobby')).toBeNull();
  });
});
