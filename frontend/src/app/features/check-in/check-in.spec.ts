import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { fakeResource } from '../../../testing/resource-fixtures';
import { RESOURCE_PHOTO_STUB } from '../../../testing/resource-photo-stub';
import { CHECK_IN_TICK_MS, CheckIn, visitFor } from './check-in';

// Local times, so what the page prints reads the same in every time zone the tests run in.
const at = (time: string) => new Date(`2026-10-07T${time}`);
const iso = (time: string) => at(time).toISOString();

/** Ten to eleven in Meeting Room A, needing a check-in: open from 09:50 until 10:15. */
function booking(overrides: Partial<Reservation> = {}): Reservation {
  return fakeReservation({
    id: 'rsv-10',
    resourceId: 'res-1',
    startDateTime: iso('10:00'),
    endDateTime: iso('11:00'),
    checkInRequired: true,
    ...overrides,
  });
}

describe('visitFor', () => {
  const now = (time: string) => at(time).getTime();

  it('offers the check-in from ten minutes before the start until fifteen after', () => {
    expect(visitFor([booking()], 'res-1', now('09:49')).kind).toBe('later');
    expect(visitFor([booking()], 'res-1', now('09:50'))).toEqual({
      kind: 'open',
      reservation: booking(),
      closesAt: at('10:15'),
    });
    expect(visitFor([booking()], 'res-1', now('10:14')).kind).toBe('open');
    expect(visitFor([booking()], 'res-1', now('10:15')).kind).toBe('none');
  });

  it('never keeps it open past the end of a short booking', () => {
    const short = booking({ endDateTime: iso('10:10') });

    expect(visitFor([short], 'res-1', now('10:05'))).toMatchObject({ closesAt: at('10:10') });
    expect(visitFor([short], 'res-1', now('10:10')).kind).toBe('none');
  });

  it('shows a checked-in booking as done until it ends, even checked in before its start', () => {
    const checkedIn = booking({ checkedInAt: iso('09:55') });

    expect(visitFor([checkedIn], 'res-1', now('09:56')).kind).toBe('checkedIn');
    expect(visitFor([checkedIn], 'res-1', now('10:59')).kind).toBe('checkedIn');
    expect(visitFor([checkedIn], 'res-1', now('11:00')).kind).toBe('none');
  });

  it('puts a booking still waiting for its check-in before one already checked in', () => {
    const first = booking({ id: 'a', endDateTime: iso('10:10'), checkedInAt: iso('09:52') });
    const second = booking({ id: 'b', startDateTime: iso('10:10') });

    expect(visitFor([first, second], 'res-1', now('10:05'))).toMatchObject({
      kind: 'open',
      reservation: { id: 'b' },
    });
  });

  it('points to the next booking whose check-in has not opened', () => {
    const later = booking({ startDateTime: iso('14:00'), endDateTime: iso('15:00') });

    expect(visitFor([later], 'res-1', now('12:00'))).toEqual({
      kind: 'later',
      reservation: later,
      opensAt: at('13:50'),
    });
  });

  it('leaves out other resources, cancelled bookings and bookings that need no check-in', () => {
    const others = [
      booking({ resourceId: 'res-2' }),
      booking({ status: 'Cancelled' }),
      booking({ checkInRequired: false }),
    ];

    expect(visitFor(others, 'res-1', now('10:00')).kind).toBe('none');
  });

  it('mentions a booking released today when nothing else is to come', () => {
    const released = booking({ status: 'Cancelled', releasedAt: iso('10:15') });
    const yesterday = booking({
      id: 'old',
      status: 'Cancelled',
      releasedAt: new Date(at('10:15').getTime() - 24 * 3600_000).toISOString(),
    });

    expect(visitFor([released], 'res-1', now('10:20'))).toEqual({
      kind: 'released',
      reservation: released,
    });
    expect(visitFor([yesterday], 'res-1', now('10:20')).kind).toBe('none');
  });
});

describe('CheckIn', () => {
  const currentUser = signal<{ id: string } | null>({ id: OWNER_ID });
  let getResource: ReturnType<typeof vi.fn>;
  let getMine: ReturnType<typeof vi.fn>;
  let checkIn: ReturnType<typeof vi.fn>;
  let updated: Subject<Reservation>;
  let fixture: ComponentFixture<CheckIn>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(at('09:55'));
    getResource = vi.fn(() => of(fakeResource({ checkInRequired: true })));
    getMine = vi.fn(() => of([booking()]));
    checkIn = vi.fn(() => of(booking({ checkedInAt: iso('09:55') })));
    updated = new Subject();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResource, checkIn } },
        { provide: ReservationService, useValue: { getMine } },
        {
          provide: ReservationHubService,
          useValue: { created$: new Subject(), updated$: updated, cancelled$: new Subject() },
        },
        { provide: AuthService, useValue: { currentUser } },
        RESOURCE_PHOTO_STUB,
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  /** `null`: opened without the code, e.g. from a bookmark. */
  async function render(code: string | null = 'c0de'): Promise<void> {
    fixture = TestBed.createComponent(CheckIn);
    fixture.componentRef.setInput('resourceId', 'res-1');
    fixture.componentRef.setInput('code', code ?? undefined);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function checkInButton(): HTMLButtonElement | null {
    return element.querySelector<HTMLButtonElement>('.check-in-button');
  }

  it('checks in the open booking with the code from the QR code', async () => {
    await render();

    expect(element.querySelector('h2')!.textContent).toBe('Meeting Room A');
    expect(element.textContent).toContain(
      'Your booking from 10:00 to 11:00 can be checked in until 10:15.',
    );

    checkInButton()!.click();
    await fixture.whenStable();

    expect(checkIn).toHaveBeenCalledWith('res-1', 'c0de');
    expect(element.textContent).toContain("You're checked in for 10:00 to 11:00.");
    expect(checkInButton()).toBeNull();
  });

  it("shows the API's reason when the check-in is refused", async () => {
    const reason = 'This QR code is no longer valid. Scan the current code for Meeting Room A.';
    checkIn.mockReturnValue(throwError(() => new ApiError(403, 'Forbidden', reason)));
    await render();

    checkInButton()!.click();
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')!.textContent).toBe(reason);
    expect(checkInButton()!.disabled).toBe(false);
  });

  it('asks to try again when the check-in never reached the server', async () => {
    checkIn.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    await render();

    checkInButton()!.click();
    await fixture.whenStable();

    expect(element.textContent).toContain('The check-in could not be sent.');
  });

  it('has no button without the code, only the way to get it', async () => {
    await render(null);

    expect(checkInButton()).toBeNull();
    expect(element.textContent).toContain('scan the QR code at the door');
  });

  it("says so for a resource that doesn't use check-in", async () => {
    getResource.mockReturnValue(of(fakeResource({ checkInRequired: false })));
    await render();

    expect(element.textContent).toContain("Meeting Room A doesn't use check-in");
    expect(checkInButton()).toBeNull();
  });

  it('opens the check-in when its time comes, without a reload', async () => {
    getMine.mockReturnValue(
      of([booking({ startDateTime: iso('10:10'), endDateTime: iso('11:00') })]),
    );
    vi.setSystemTime(at('09:58'));
    await render();
    expect(element.textContent).toContain('Your booking at 10:10 can be checked in from 10:00.');

    vi.setSystemTime(at('10:00'));
    vi.advanceTimersByTime(CHECK_IN_TICK_MS);
    await fixture.whenStable();

    expect(checkInButton()).not.toBeNull();
  });

  it('follows the booking when it changes elsewhere, and only the caller’s', async () => {
    await render();

    updated.next(booking({ userId: 'someone-else', checkedInAt: iso('09:56') }));
    await fixture.whenStable();
    expect(checkInButton()).not.toBeNull();

    updated.next(booking({ checkedInAt: iso('09:56') }));
    await fixture.whenStable();
    expect(element.textContent).toContain("You're checked in");
  });

  it('offers to book when there is nothing to check in to', async () => {
    getMine.mockReturnValue(of([]));
    await render();

    expect(element.textContent).toContain('You have no booking of Meeting Room A to check in to.');
    const book = element.querySelector<HTMLAnchorElement>('a.btn')!;
    expect(book.getAttribute('href')).toBe('/reservations/new?resourceId=res-1');
  });

  it('says so when the resource could not be loaded', async () => {
    getResource.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(element.textContent).toContain('This resource could not be loaded.');
  });
});
