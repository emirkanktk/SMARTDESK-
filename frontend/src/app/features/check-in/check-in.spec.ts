import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { fakeResource } from '../../../testing/resource-fixtures';
import { RESOURCE_PHOTO_STUB } from '../../../testing/resource-photo-stub';
import { CHECK_IN_TICK_MS, CheckIn, roomNowFor, visitFor } from './check-in';

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
    expect(visitFor([booking()], 'res-1', now('10:15')).kind).toBe('closed');
  });

  it('says the check-in has closed while the booking waits to be released', () => {
    expect(visitFor([booking()], 'res-1', now('10:16'))).toEqual({
      kind: 'closed',
      reservation: booking(),
      closedAt: at('10:15'),
    });
    expect(visitFor([booking()], 'res-1', now('10:59')).kind).toBe('closed');
    expect(visitFor([booking()], 'res-1', now('11:00')).kind).toBe('none');
  });

  it('puts a booking whose check-in just closed before the next one', () => {
    const later = booking({ id: 'later', startDateTime: iso('14:00'), endDateTime: iso('15:00') });

    expect(visitFor([later, booking()], 'res-1', now('10:20'))).toMatchObject({
      kind: 'closed',
      reservation: { id: 'rsv-10' },
    });
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

describe('roomNowFor', () => {
  const now = at('10:00').getTime();
  const room = fakeResource();
  const other = (start: string, end: string) =>
    booking({
      id: `${start}`,
      userId: 'someone-else',
      startDateTime: iso(start),
      endDateTime: iso(end),
    });

  it('is free until the next booking or block, or all day', () => {
    expect(roomNowFor(room, { bookings: [], blocks: [] }, now)).toEqual({
      kind: 'free',
      until: null,
    });
    expect(roomNowFor(room, { bookings: [other('11:00', '12:00')], blocks: [] }, now)).toEqual({
      kind: 'free',
      until: at('11:00'),
    });
  });

  it('is in use until the current booking ends, and a cancelled one does not count', () => {
    expect(roomNowFor(room, { bookings: [other('09:30', '10:30')], blocks: [] }, now)).toEqual({
      kind: 'busy',
      until: at('10:30'),
    });
    expect(
      roomNowFor(
        room,
        { bookings: [{ ...other('09:30', '10:30'), status: 'Cancelled' }], blocks: [] },
        now,
      ).kind,
    ).toBe('free');
  });

  it('is out of use when the resource is, or a block covers now', () => {
    expect(
      roomNowFor(fakeResource({ status: 'Maintenance' }), { bookings: [], blocks: [] }, now).kind,
    ).toBe('unavailable');
    const block = {
      id: 'b',
      resourceId: 'res-1',
      resourceName: 'A',
      startDateTime: iso('09:00'),
      endDateTime: iso('10:01'),
      reason: 'x',
      createdByUserId: 'u',
    };
    expect(roomNowFor(room, { bookings: [], blocks: [block] }, now).kind).toBe('unavailable');
  });
});

describe('CheckIn', () => {
  const currentUser = signal<{ id: string } | null>({ id: OWNER_ID });
  let getResource: ReturnType<typeof vi.fn>;
  let getMine: ReturnType<typeof vi.fn>;
  let getInvited: ReturnType<typeof vi.fn>;
  let getCalendar: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let end: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let checkIn: ReturnType<typeof vi.fn>;
  let updated: Subject<Reservation>;
  let fixture: ComponentFixture<CheckIn>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(at('09:55'));
    getResource = vi.fn(() => of(fakeResource({ checkInRequired: true })));
    getMine = vi.fn(() => of([booking()]));
    getInvited = vi.fn(() => of([]));
    getCalendar = vi.fn(() => of([]));
    getBlocks = vi.fn(() => of([]));
    create = vi.fn((body: { startDateTime: string; endDateTime: string }) =>
      of(
        booking({
          id: 'rsv-new',
          startDateTime: body.startDateTime,
          endDateTime: body.endDateTime,
        }),
      ),
    );
    end = vi.fn((id: string) => of(booking({ id, endDateTime: new Date().toISOString() })));
    success = vi.fn();
    checkIn = vi.fn(() => of(booking({ checkedInAt: iso('09:55') })));
    updated = new Subject();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResource, checkIn } },
        {
          provide: ReservationService,
          useValue: {
            getMine,
            getInvited,
            getCalendar,
            create,
            end,
            getExtensionOptions: () => of({ minutes: [15, 30] }),
          },
        },
        { provide: ResourceBlockService, useValue: { getBlocks } },
        { provide: NotificationService, useValue: { success } },
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

    updated.next(booking({ id: 'rsv-11', userId: 'someone-else', checkedInAt: iso('09:56') }));
    await fixture.whenStable();
    expect(checkInButton()).not.toBeNull();

    updated.next(booking({ checkedInAt: iso('09:56') }));
    await fixture.whenStable();
    expect(element.textContent).toContain("You're checked in");
  });

  it('says a booking whose check-in closed is about to be released, and offers no way to book', async () => {
    vi.setSystemTime(at('10:20'));
    await render();

    expect(element.textContent).toContain(
      "The check-in for your booking at 10:00 closed at 10:15 with nobody checked in, so it's about to be released.",
    );
    expect(checkInButton()).toBeNull();
    expect(element.querySelector('a.btn')).toBeNull();
  });

  describe('a meeting the caller is invited to (ADR-0032)', () => {
    const meeting = (overrides: Partial<Reservation> = {}) =>
      booking({
        id: 'rsv-20',
        userId: 'u-zeynep',
        userDisplayName: 'Zeynep Kaya',
        attendees: [{ id: OWNER_ID, name: 'Ayşe Yılmaz', response: 'Pending' }],
        ...overrides,
      });

    beforeEach(() => {
      getMine.mockReturnValue(of([]));
      getInvited.mockReturnValue(of([meeting()]));
    });

    it('can be checked in by them too', async () => {
      checkIn.mockReturnValue(of(meeting({ checkedInAt: iso('09:55') })));
      await render();

      expect(element.textContent).toContain(
        "The meeting you're invited to, from 10:00 to 11:00, can be checked in until 10:15.",
      );
      checkInButton()!.click();
      await fixture.whenStable();

      expect(checkIn).toHaveBeenCalledWith('res-1', 'c0de');
      expect(element.textContent).toContain(
        'The meeting from 10:00 to 11:00 is checked in, so the room is kept for it.',
      );
    });

    it('says when its check-in opens', async () => {
      getInvited.mockReturnValue(
        of([meeting({ startDateTime: iso('14:00'), endDateTime: iso('15:00') })]),
      );
      await render();

      expect(element.textContent).toContain(
        "The meeting you're invited to at 14:00 can be checked in from 13:50.",
      );
    });

    it('shows when someone else of the meeting checked in first', async () => {
      await render();
      updated.next(meeting({ checkedInAt: iso('09:56') }));
      await fixture.whenStable();

      expect(element.textContent).toContain('is checked in, so the room is kept for it.');
      expect(checkInButton()).toBeNull();
    });

    it('goes away when they are taken off the list', async () => {
      await render();
      updated.next(meeting({ attendees: [] }));
      await fixture.whenStable();

      expect(element.textContent).toContain(
        'You have no booking of Meeting Room A to check in to.',
      );
    });

    it('says so when it was released because nobody checked in', async () => {
      vi.setSystemTime(at('10:20'));
      await render();
      updated.next(meeting({ status: 'Cancelled', releasedAt: iso('10:15') }));
      await fixture.whenStable();

      expect(element.textContent).toContain(
        'The meeting you were invited to at 10:00 was released at 10:15 because nobody checked in.',
      );
    });

    it('says when its check-in closed and it is about to be released', async () => {
      vi.setSystemTime(at('10:20'));
      await render();

      expect(element.textContent).toContain(
        "The check-in for the meeting you're invited to at 10:00 closed at 10:15 with nobody checked in, so it's about to be released.",
      );
      expect(checkInButton()).toBeNull();
    });
  });

  it('offers to book when there is nothing to check in to', async () => {
    getMine.mockReturnValue(of([]));
    await render();

    expect(element.textContent).toContain('You have no booking of Meeting Room A to check in to.');
    expect(element.textContent).toContain('Meeting Room A is free for the rest of the day.');
    const later = element.querySelector<HTMLAnchorElement>('a.btn')!;
    expect(later.textContent!.trim()).toBe('Book it for another time');
    expect(later.getAttribute('href')).toBe('/reservations/new?resourceId=res-1');
  });

  describe('at the door (ADR-0034)', () => {
    const buttons = () =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).map((b) =>
        b.textContent!.trim(),
      );

    function button(label: string): HTMLButtonElement {
      return Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
        (b) => b.textContent!.trim() === label,
      )!;
    }

    it('takes the code out of the address as soon as it has read it', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      await render();

      expect(navigate).toHaveBeenCalledWith(
        [],
        expect.objectContaining({ queryParams: { code: null }, replaceUrl: true }),
      );
      fixture.componentRef.setInput('code', undefined); // the address no longer has it
      await fixture.whenStable();
      checkInButton()!.click();
      await fixture.whenStable();
      expect(checkIn).toHaveBeenCalledWith('res-1', 'c0de');
    });

    it('says who checked in when it was someone else of the meeting', async () => {
      getMine.mockReturnValue(
        of([
          booking({
            checkedInAt: iso('09:52'),
            checkedInBy: 'u-mehmet',
            checkedInByName: 'Mehmet Demir',
          }),
        ]),
      );
      await render();

      expect(element.textContent).toContain('Checked in by Mehmet Demir at 09:52.');
    });

    it('lets the owner end a booking under way, and the room is free again', async () => {
      vi.setSystemTime(at('10:20'));
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      getMine.mockReturnValue(of([booking({ checkedInAt: iso('09:58') })]));
      await render();

      button("We're done: free the room").click();
      await fixture.whenStable();

      expect(end).toHaveBeenCalledWith('rsv-10');
      expect(success).toHaveBeenCalledWith('Meeting Room A is free again.');
      expect(element.textContent).toContain('Meeting Room A is free for the rest of the day.');
    });

    it('offers to run longer from the door', async () => {
      vi.setSystemTime(at('10:20'));
      getMine.mockReturnValue(of([booking({ checkedInAt: iso('09:58') })]));
      await render();

      button('Need longer?').click();
      await fixture.whenStable();

      expect(element.querySelector('app-extend-booking')).not.toBeNull();
      expect(buttons()).toContain('+15 min');
    });

    it('offers no ending before the start', async () => {
      getMine.mockReturnValue(of([booking({ checkedInAt: iso('09:52') })]));
      await render(); // 09:55: checked in early

      expect(buttons()).not.toContain("We're done: free the room");
    });

    it('books the free room on the spot and checks in at once', async () => {
      getMine.mockReturnValue(of([]));
      await render();

      button('30 min').click();
      await fixture.whenStable();

      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          resourceId: 'res-1',
          startDateTime: at('09:55').toISOString(),
          endDateTime: at('10:25').toISOString(),
        }),
      );
      expect(success).toHaveBeenCalledWith('Meeting Room A is yours until 10:25.');
      expect(checkIn).toHaveBeenCalledWith('res-1', 'c0de');
    });

    it('offers only what fits before the next booking', async () => {
      getMine.mockReturnValue(of([]));
      getCalendar.mockReturnValue(
        of([
          booking({
            id: 'other',
            userId: 'someone-else',
            startDateTime: iso('10:40'),
            endDateTime: iso('11:30'),
          }),
        ]),
      );
      await render();

      expect(element.textContent).toContain('Meeting Room A is free until 10:40.');
      expect(buttons()).toContain('30 min');
      expect(buttons()).not.toContain('1 hour');
    });

    it('shows the room in use by someone else, with nothing to book now', async () => {
      getMine.mockReturnValue(of([]));
      getCalendar.mockReturnValue(
        of([
          booking({
            id: 'other',
            userId: 'someone-else',
            startDateTime: iso('09:30'),
            endDateTime: iso('11:00'),
          }),
        ]),
      );
      await render();

      expect(element.textContent).toContain('Meeting Room A is in use until 11:00.');
      expect(buttons()).not.toContain('30 min');
    });

    it('says the room cannot be booked during a blocked window', async () => {
      getMine.mockReturnValue(of([]));
      getBlocks.mockReturnValue(
        of([
          {
            id: 'b',
            resourceId: 'res-1',
            resourceName: 'Meeting Room A',
            startDateTime: iso('09:00'),
            endDateTime: iso('12:00'),
            reason: 'Cleaning',
            createdByUserId: 'u',
          },
        ]),
      );
      await render();

      expect(element.textContent).toContain("Meeting Room A can't be booked right now.");
    });
  });

  it('says so when the resource could not be loaded', async () => {
    getResource.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(element.textContent).toContain('This resource could not be loaded.');
  });
});
