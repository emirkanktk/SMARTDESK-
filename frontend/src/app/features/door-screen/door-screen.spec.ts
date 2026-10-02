import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { DoorScreen as DoorScreenData } from '../../core/models/door-screen.model';
import { DoorScreenService } from '../../core/services/door-screen.service';
import { DOOR_REFRESH_MS, DoorScreen, doorState, nextRefreshMs } from './door-screen';

// Local times, so what the screen prints reads the same in every time zone the tests run in.
const at = (time: string) => new Date(`2026-10-07T${time}`);
const iso = (time: string) => at(time).toISOString();
const TOKEN = 'a'.repeat(64);

function screen(overrides: Partial<DoorScreenData> = {}): DoorScreenData {
  return {
    resourceName: 'Board Room',
    location: '5th Floor',
    capacity: 8,
    status: 'Active',
    now: iso('10:00'),
    bookings: [],
    blocks: [],
    checkIn: {
      url: 'http://localhost:4200/check-in/res-1?code=0123456789abcdef0123',
      changesAt: iso('10:00:20'),
    },
    ...overrides,
  };
}

const booking = (start: string, end: string, checkedIn = false) => ({
  startDateTime: iso(start),
  endDateTime: iso(end),
  checkInNeeded: true,
  checkedIn,
});

describe('doorState', () => {
  const now = at('10:05').getTime();

  it('is free until the next booking or block, or for the rest of the day', () => {
    expect(doorState(screen(), now)).toEqual({ kind: 'free', until: null });
    expect(doorState(screen({ bookings: [booking('11:00', '12:00')] }), now)).toEqual({
      kind: 'free',
      until: at('11:00'),
    });
    expect(
      doorState(
        screen({
          blocks: [{ startDateTime: iso('10:30'), endDateTime: iso('11:00'), reason: 'x' }],
        }),
        now,
      ),
    ).toEqual({ kind: 'free', until: at('10:30') });
  });

  it('is in use, waiting for a check-in until its window closes', () => {
    expect(doorState(screen({ bookings: [booking('10:00', '11:00')] }), now)).toEqual({
      kind: 'busy',
      booking: booking('10:00', '11:00'),
      until: at('11:00'),
      waitingUntil: at('10:15'),
    });
    // A short booking's window never outlasts it.
    expect(doorState(screen({ bookings: [booking('10:00', '10:10')] }), now)).toMatchObject({
      waitingUntil: at('10:10'),
    });
  });

  it('waits for nobody once checked in, or once the window has closed', () => {
    expect(doorState(screen({ bookings: [booking('10:00', '11:00', true)] }), now)).toMatchObject({
      kind: 'busy',
      waitingUntil: null,
    });
    expect(doorState(screen({ bookings: [booking('09:40', '11:00')] }), now)).toMatchObject({
      waitingUntil: null,
    });
  });

  it('is not available while the resource is out of use or blocked', () => {
    expect(doorState(screen({ status: 'Maintenance' }), now)).toEqual({
      kind: 'unavailable',
      reason: 'Maintenance',
      until: null,
    });
    expect(
      doorState(
        screen({
          blocks: [{ startDateTime: iso('10:00'), endDateTime: iso('12:00'), reason: 'Cleaning' }],
        }),
        now,
      ),
    ).toEqual({ kind: 'unavailable', reason: 'Cleaning', until: at('12:00') });
  });
});

describe('nextRefreshMs', () => {
  it('asks again just after the code changes, at most every ten seconds', () => {
    expect(nextRefreshMs(screen({ checkIn: null }))).toBe(DOOR_REFRESH_MS);
    expect(nextRefreshMs(screen({ checkIn: { url: 'u', changesAt: iso('10:00:03') } }))).toBe(
      3_500,
    );
    expect(nextRefreshMs(screen({ checkIn: { url: 'u', changesAt: iso('10:00:25') } }))).toBe(
      DOOR_REFRESH_MS,
    );
    expect(nextRefreshMs(screen({ checkIn: { url: 'u', changesAt: iso('09:59:59') } }))).toBe(
      1_000,
    );
  });
});

describe('DoorScreen', () => {
  let get: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<DoorScreen>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({
      toFake: ['Date', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'],
    });
    vi.setSystemTime(at('10:05'));
    localStorage.clear();
    window.history.replaceState(null, '', '/door/res-1');
    get = vi.fn(() =>
      of(
        screen({
          now: iso('10:05'),
          bookings: [booking('10:00', '11:00'), booking('14:00', '15:00')],
        }),
      ),
    );
    TestBed.configureTestingModule({
      providers: [{ provide: DoorScreenService, useValue: { get } }],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(DoorScreen);
    fixture.componentRef.setInput('resourceId', 'res-1');
    element = fixture.nativeElement;
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('keeps the token from its link, and takes it out of the address', async () => {
    window.history.replaceState(null, '', `/door/res-1#${TOKEN}`);
    await render();

    expect(get).toHaveBeenCalledWith('res-1', TOKEN, expect.any(String));
    expect(window.location.hash).toBe('');
    expect(localStorage.getItem('smartdesk_door_res-1')).toBe(TOKEN);
  });

  it('uses the token kept from an earlier visit', async () => {
    localStorage.setItem('smartdesk_door_res-1', TOKEN);
    await render();

    expect(get).toHaveBeenCalledWith('res-1', TOKEN, expect.any(String));
  });

  it('says it is not set up without a token, and asks nothing', async () => {
    await render();

    expect(text()).toContain("This screen isn't set up");
    expect(get).not.toHaveBeenCalled();
  });

  it('shows the room, whether it is in use, the rest of the day and the QR code', async () => {
    localStorage.setItem('smartdesk_door_res-1', TOKEN);
    await render();

    expect(element.querySelector('h1')!.textContent).toBe('Board Room');
    expect(text()).toContain('In use');
    expect(text()).toContain('until 11:00');
    expect(text()).toContain('Without a check-in by 10:15, the room is released.');
    expect(Array.from(element.querySelectorAll('.today li')).map((li) => li.textContent)).toEqual([
      '14:00-15:00',
    ]);
    expect(element.querySelector('.scan')!.getAttribute('data-url')).toBe(screen().checkIn!.url);
    expect(element.querySelector('app-qr-code svg')).not.toBeNull();
  });

  it('asks again when its code is about to change', async () => {
    localStorage.setItem('smartdesk_door_res-1', TOKEN);
    get.mockReturnValue(
      of(screen({ now: iso('10:05'), checkIn: { url: 'u1', changesAt: iso('10:05:04') } })),
    );
    await render();
    expect(get).toHaveBeenCalledTimes(1);

    get.mockReturnValue(
      of(screen({ now: iso('10:05:05'), checkIn: { url: 'u2', changesAt: iso('10:05:34') } })),
    );
    vi.advanceTimersByTime(4_500);
    fixture.detectChanges();

    expect(get).toHaveBeenCalledTimes(2);
    expect(element.querySelector('.scan')!.getAttribute('data-url')).toBe('u2');
  });

  it('forgets a token that is no longer the screen’s', async () => {
    localStorage.setItem('smartdesk_door_res-1', TOKEN);
    get.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(text()).toContain("This screen isn't set up");
    expect(localStorage.getItem('smartdesk_door_res-1')).toBeNull();
  });

  it('keeps what it showed while the server cannot be reached, and tries again', async () => {
    localStorage.setItem('smartdesk_door_res-1', TOKEN);
    await render();
    get.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));

    vi.advanceTimersByTime(DOOR_REFRESH_MS);
    fixture.detectChanges();
    expect(text()).toContain("Can't reach SmartDesk right now");
    expect(element.querySelector('h1')!.textContent).toBe('Board Room');

    get.mockReturnValue(of(screen({ now: iso('10:05:20') })));
    vi.advanceTimersByTime(DOOR_REFRESH_MS);
    fixture.detectChanges();
    expect(text()).not.toContain("Can't reach SmartDesk right now");
  });
});
