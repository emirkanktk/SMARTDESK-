import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation, ReservationAttendee } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { ATTENDEES_SHOWN, MyReservations } from './my-reservations';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('MyReservations', () => {
  const NOW = new Date('2026-10-05T10:07'); // local time

  const booking = (id: string, resourceName: string, start: string, end: string, extra = {}) =>
    fakeReservation({
      id,
      resourceName,
      startDateTime: utc(start),
      endDateTime: utc(end),
      ...extra,
    });
  const soon = booking('rsv-1', 'Meeting Room A', '2026-10-06T14:00', '2026-10-06T15:30');
  const running = booking('rsv-2', 'Board Room', '2026-10-05T09:30', '2026-10-05T11:00', {
    note: null,
  });
  const later = booking('rsv-3', 'Desk 12', '2026-10-08T09:00', '2026-10-09T09:00', { note: null });
  const lastWeek = booking('rsv-4', 'Camera C-1', '2026-10-01T10:00', '2026-10-01T11:00');
  const friday = booking('rsv-5', 'Projector P-1', '2026-10-03T10:00', '2026-10-03T11:00');
  const called = booking('rsv-6', 'Company Car', '2026-10-07T08:00', '2026-10-07T18:00', {
    status: 'Cancelled',
  });

  let getMine: ReturnType<typeof vi.fn>;
  let getInvited: ReturnType<typeof vi.fn>;
  let respond: ReturnType<typeof vi.fn>;
  let cancel: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<MyReservations>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getMine = vi.fn(() => of([lastWeek, friday, running, soon, called, later]));
    getInvited = vi.fn(() => of([]));
    respond = vi.fn();
    cancel = vi.fn((id: string) => of({ ...soon, id, status: 'Cancelled' }));
    success = vi.fn();
    error = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };

    await TestBed.configureTestingModule({
      imports: [MyReservations],
      providers: [
        provideRouter([]),
        { provide: ReservationService, useValue: { getMine, getInvited, respond, cancel } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser: signal({ id: OWNER_ID }) } },
        { provide: NotificationService, useValue: { success, error } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    confirmSpy.mockRestore();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(MyReservations);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function names(): string[] {
    return Array.from(element.querySelectorAll('.item h2')).map((h) => h.textContent!.trim());
  }

  async function openTab(label: string): Promise<void> {
    Array.from(element.querySelectorAll<HTMLButtonElement>('[role=tab]'))
      .find((t) => t.textContent!.includes(label))!
      .click();
    await fixture.whenStable();
  }

  function tabText(label: string): string {
    return Array.from(element.querySelectorAll('[role=tab]'))
      .find((t) => t.textContent!.includes(label))!
      .textContent!.replace(/\s+/g, ' ')
      .trim();
  }

  async function clickCancelOn(name: string): Promise<void> {
    const item = Array.from(element.querySelectorAll('.item')).find((i) =>
      i.querySelector('h2')!.textContent!.includes(name),
    )!;
    item.querySelector<HTMLButtonElement>('button.cancel')!.click();
    await fixture.whenStable();
  }

  it('lists upcoming reservations soonest first, including the one in progress', async () => {
    await render();

    expect(names()).toEqual(['Board Room', 'Meeting Room A', 'Desk 12']);
    expect(element.querySelector('.item')!.textContent).toContain('In progress');
  });

  it('shows the date, time range, duration and note', async () => {
    await render();
    const card = element.querySelectorAll('.item')[1].textContent!.replace(/\s+/g, ' ');

    expect(card).toContain('Tue 6 Oct');
    expect(card).toContain('14:00-15:30');
    expect(card).toContain('1 h 30 min');
    expect(card).toContain('Meeting Room · 2nd Floor');
    expect(card).toContain('Sprint planning');
  });

  it('says which upcoming reservations have a reminder', async () => {
    getMine.mockReturnValue(
      of([
        { ...soon, reminderMinutes: 15 },
        { ...running, reminderMinutes: 5 }, // started: too late for a reminder
        { ...later, reminderMinutes: null },
        { ...called, reminderMinutes: 30 },
      ]),
    );
    await render();

    const reminders = Array.from(element.querySelectorAll('.item')).map(
      (item) => item.querySelector('.reminder')?.textContent ?? null,
    );
    expect(reminders).toEqual([null, 'Reminder 15 min before', null]); // Board Room, Room A, Desk
    await openTab('Cancelled');
    expect(element.querySelector('.reminder')).toBeNull();
  });

  it('shows both dates for a reservation that spans days', async () => {
    await render();
    const card = element.querySelectorAll('.item')[2].textContent!.replace(/\s+/g, ' ');

    expect(card).toContain('09:00 to Fri 9 Oct, 09:00');
    expect(card).toContain('1 day');
  });

  it('counts each tab', async () => {
    await render();

    expect(tabText('Upcoming')).toBe('Upcoming 3');
    expect(tabText('Invitations')).toBe('Invitations 0');
    expect(tabText('Past')).toBe('Past 2');
    expect(tabText('Cancelled')).toBe('Cancelled 1');
  });

  it('shows past reservations most recent first, without actions', async () => {
    await render();
    await openTab('Past');

    expect(names()).toEqual(['Projector P-1', 'Camera C-1']);
    expect(element.querySelector('.actions')).toBeNull();
  });

  it('shows cancelled reservations', async () => {
    await render();
    await openTab('Cancelled');

    expect(names()).toEqual(['Company Car']);
    expect(element.querySelector('.item')!.textContent).toContain('Cancelled');
  });

  it('links Change to the edit form', async () => {
    await render();
    const link = element.querySelectorAll<HTMLAnchorElement>('.item a')[1];

    expect(link.textContent!.trim()).toBe('Change');
    expect(link.getAttribute('href')).toBe('/reservations/rsv-1/edit');
  });

  it('cancels after confirmation and moves the reservation to Cancelled', async () => {
    await render();
    await clickCancelOn('Meeting Room A');

    expect(confirmSpy.mock.calls[0][0]).toContain(
      'Meeting Room A reservation for Tue 6 Oct, 14:00-15:30',
    );
    expect(cancel).toHaveBeenCalledWith('rsv-1');
    expect(names()).toEqual(['Board Room', 'Desk 12']);
    expect(tabText('Cancelled')).toBe('Cancelled 2');
    expect(success.mock.calls[0][0]).toContain('was cancelled');
  });

  it('does nothing when the confirmation is declined', async () => {
    confirmSpy.mockReturnValue(false);
    await render();
    await clickCancelOn('Meeting Room A');

    expect(cancel).not.toHaveBeenCalled();
    expect(names()).toContain('Meeting Room A');
  });

  it('shows the API message when it can no longer be cancelled, and refreshes', async () => {
    cancel.mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', 'This reservation has already ended.')),
    );
    await render();
    await clickCancelOn('Board Room');

    expect(error).toHaveBeenCalledWith('This reservation has already ended.');
    expect(getMine).toHaveBeenCalledTimes(2);
  });

  it('offers a retry when loading fails', async () => {
    getMine.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain('Your reservations could not be loaded.');
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(names()).toEqual(['Board Room', 'Meeting Room A', 'Desk 12']);
  });

  it('points to the availability search when nothing is upcoming', async () => {
    getMine.mockReturnValue(of([lastWeek]));
    await render();

    expect(element.textContent).toContain('You have no upcoming reservations.');
    expect(element.querySelector('.empty a')!.getAttribute('href')).toBe('/availability');
  });

  describe('live updates', () => {
    it('adds a reservation booked elsewhere', async () => {
      await render();
      hub.created$.next(booking('rsv-9', 'Desk 7', '2026-10-06T09:00', '2026-10-06T10:00'));
      await fixture.whenStable();

      expect(names()).toEqual(['Board Room', 'Desk 7', 'Meeting Room A', 'Desk 12']);
    });

    it("ignores other people's reservations", async () => {
      await render();
      hub.created$.next(
        booking('rsv-9', 'Desk 7', '2026-10-06T09:00', '2026-10-06T10:00', { userId: 'other' }),
      );
      await fixture.whenStable();

      expect(names()).not.toContain('Desk 7');
    });

    it('moves a reservation cancelled elsewhere, e.g. by an admin', async () => {
      await render();
      hub.cancelled$.next({ ...soon, status: 'Cancelled' });
      await fixture.whenStable();

      expect(names()).toEqual(['Board Room', 'Desk 12']);
      expect(tabText('Cancelled')).toBe('Cancelled 2');
    });

    it('applies a change made elsewhere', async () => {
      await render();
      hub.updated$.next({ ...soon, resourceName: 'Meeting Room B' });
      await fixture.whenStable();

      expect(names()).toEqual(['Board Room', 'Meeting Room B', 'Desk 12']);
    });

    it('keeps a change that arrives while the list is still loading', async () => {
      const response = new Subject<Reservation[]>();
      getMine.mockReturnValue(response);
      await render();

      hub.created$.next(booking('rsv-9', 'Desk 7', '2026-10-06T09:00', '2026-10-06T10:00'));
      response.next([soon]); // queried before rsv-9 was committed
      response.complete();
      await fixture.whenStable();

      expect(names()).toEqual(['Desk 7', 'Meeting Room A']);
    });

    it('uses only the newest load when a reconnect overlaps one', async () => {
      const slow = new Subject<Reservation[]>();
      getMine.mockReturnValueOnce(slow).mockReturnValue(of([soon]));
      await render();
      hub.reconnected$.next();
      await fixture.whenStable();

      slow.next([later]); // the first answer arrives last
      await fixture.whenStable();

      expect(names()).toEqual(['Meeting Room A']);
    });

    it('refetches after the connection comes back', async () => {
      await render();
      getMine.mockReturnValue(of([soon]));
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(getMine).toHaveBeenCalledTimes(2);
      expect(names()).toEqual(['Meeting Room A']);
    });
  });

  describe('meetings the user is invited to (ADR-0032)', () => {
    const invitation = (extra: Partial<Reservation> = {}) =>
      booking('rsv-20', 'Planning Room', '2026-10-06T11:00', '2026-10-06T12:00', {
        userId: 'u-zeynep',
        userDisplayName: 'Zeynep Kaya',
        note: 'Quarterly plan',
        reminderMinutes: 15,
        attendees: [{ id: OWNER_ID, name: 'Ayşe Yılmaz', response: 'Pending' }],
        ...extra,
      });

    function itemText(): string {
      return element.querySelector('.item')!.textContent!.replace(/\s+/g, ' ');
    }

    it('lists them under Invitations, with who organized each, and only a way to see it', async () => {
      getInvited.mockReturnValue(of([invitation()]));
      await render();

      expect(names()).not.toContain('Planning Room');
      expect(tabText('Invitations')).toBe('Invitations 1');
      await openTab('Invitations');

      expect(names()).toEqual(['Planning Room']);
      expect(itemText()).toContain('Organized by Zeynep Kaya');
      expect(itemText()).toContain('Quarterly plan');
      expect(itemText()).not.toContain('Reminder'); // the organizer's, not theirs
      const links = element.querySelectorAll<HTMLAnchorElement>('.item .actions a');
      expect(links).toHaveLength(1);
      expect(links[0].textContent!.trim()).toBe('Details');
      expect(links[0].getAttribute('href')).toBe('/reservations/rsv-20/edit');
      expect(element.querySelector('.item button.cancel')).toBeNull();
    });

    it('tells them when to check in, since they may do it too', async () => {
      getInvited.mockReturnValue(of([invitation({ checkInRequired: true })]));
      await render();
      await openTab('Invitations');

      expect(itemText()).toContain('Check in at the door 10:50-11:15');
    });

    it('says so when there are none', async () => {
      await render();
      await openTab('Invitations');

      expect(element.textContent).toContain(
        "Nobody has invited you to a meeting that's still to come.",
      );
    });

    it("names who is invited to the user's own bookings by their answer, the first few of each", async () => {
      const pending = ['Can Öztürk', 'Deniz Ak', 'Elif Şahin', 'Fatma Koç', 'Gül Ay'].map(
        (name, i): ReservationAttendee => ({ id: `u-${i}`, name, response: 'Pending' }),
      );
      getMine.mockReturnValue(
        of([
          {
            ...soon,
            attendees: [
              { id: 'u-a', name: 'Ayşe Yılmaz', response: 'Accepted' },
              { id: 'u-m', name: 'Mehmet Demir', response: 'Declined' },
              pending[0],
            ],
          },
          { ...later, attendees: [...pending, { id: 'u-gone', name: null, response: 'Pending' }] },
          running,
        ]),
      );
      await render();

      const lines = Array.from(element.querySelectorAll('.item')).map(
        (item) => item.querySelector('.attendees')?.textContent ?? null,
      );
      expect(ATTENDEES_SHOWN).toBe(3);
      expect(lines).toEqual([
        null, // Board Room
        "Coming: Ayşe Yılmaz · Can't come: Mehmet Demir · No answer yet: Can Öztürk",
        'No answer yet: Can Öztürk, Deniz Ak, Elif Şahin and 3 more',
      ]);
    });

    it('lets them answer an invitation right there', async () => {
      getInvited.mockReturnValue(of([invitation()]));
      respond.mockReturnValue(
        of(
          invitation({ attendees: [{ id: OWNER_ID, name: 'Ayşe Yılmaz', response: 'Accepted' }] }),
        ),
      );
      await render();
      await openTab('Invitations');
      expect(itemText()).toContain('Will you come?');

      Array.from(element.querySelectorAll<HTMLButtonElement>('app-meeting-answer button'))
        .find((b) => b.textContent!.trim() === "I'm coming")!
        .click();
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-20', { response: 'Accepted' });
      expect(itemText()).toContain("You said you're coming.");
    });

    it('has no answer to give on their own bookings', async () => {
      await render();

      expect(element.querySelector('app-meeting-answer')).toBeNull();
    });

    it('adds an invitation sent meanwhile, and drops it when the user is taken off', async () => {
      await render();

      hub.created$.next(invitation());
      await fixture.whenStable();
      expect(tabText('Invitations')).toBe('Invitations 1');

      hub.updated$.next(
        invitation({ attendees: [{ id: 'u-other', name: 'Can Öztürk', response: 'Pending' }] }),
      );
      await fixture.whenStable();
      expect(tabText('Invitations')).toBe('Invitations 0');
    });

    it('drops a meeting that is cancelled, and follows one that moves', async () => {
      getInvited.mockReturnValue(of([invitation()]));
      await render();
      await openTab('Invitations');

      hub.updated$.next(invitation({ resourceName: 'Board Room' }));
      await fixture.whenStable();
      expect(names()).toEqual(['Board Room']);

      hub.cancelled$.next(invitation({ status: 'Cancelled' }));
      await fixture.whenStable();
      expect(names()).toEqual([]);
    });

    it('ignores other meetings the user is not invited to', async () => {
      await render();
      hub.created$.next(invitation({ attendees: [] }));
      await fixture.whenStable();

      expect(tabText('Invitations')).toBe('Invitations 0');
      expect(names()).not.toContain('Planning Room');
    });

    it('keeps an invitation that arrives while the lists are still loading', async () => {
      const response = new Subject<Reservation[]>();
      getInvited.mockReturnValue(response);
      await render();

      hub.created$.next(invitation());
      response.next([]); // queried before the invitation was committed
      response.complete();
      await fixture.whenStable();

      expect(tabText('Invitations')).toBe('Invitations 1');
    });

    it('lets an invitation go once the meeting is over', async () => {
      getInvited.mockReturnValue(of([invitation()]));
      await render();
      expect(tabText('Invitations')).toBe('Invitations 1');

      // A refresh re-reads the clock, as the page does every minute; the list it gets still
      // has the meeting, which ended just now.
      vi.setSystemTime(new Date('2026-10-06T12:00'));
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(tabText('Invitations')).toBe('Invitations 0');
    });
  });

  describe('check-in at the door (ADR-0030)', () => {
    function itemText(name: string): string {
      return Array.from(element.querySelectorAll('.item'))
        .find((i) => i.querySelector('h2')!.textContent!.includes(name))!
        .textContent!.replace(/\s+/g, ' ');
    }

    it('says when to check in, until that is done or has passed', async () => {
      // It's 10:07: Desk 7's check-in is open until 10:25, but done already.
      const checkedIn = booking('rsv-7', 'Desk 7', '2026-10-05T10:10', '2026-10-05T11:00', {
        checkInRequired: true,
        checkedInAt: utc('2026-10-05T10:05'),
      });
      getMine.mockReturnValue(
        of([
          { ...soon, checkInRequired: true },
          { ...running, checkInRequired: true },
          checkedIn,
          { ...later, checkInRequired: false },
        ]),
      );
      await render();

      expect(itemText('Meeting Room A')).toContain('Check in at the door 13:50-14:15');
      expect(itemText('Desk 7')).toContain('Checked in');
      expect(itemText('Desk 7')).not.toContain('Check in at the door');
      expect(itemText('Board Room')).not.toContain('Check in at the door'); // closed at 09:45
      expect(itemText('Desk 12')).not.toContain('Check in');
    });

    it('marks a booking released because nobody checked in, and when', async () => {
      getMine.mockReturnValue(
        of([
          {
            ...soon,
            status: 'Cancelled',
            checkInRequired: true,
            releasedAt: utc('2026-10-05T09:45'),
          },
        ]),
      );
      await render();
      await openTab('Cancelled');

      expect(itemText('Meeting Room A')).toContain('Released');
      expect(itemText('Meeting Room A')).toContain('Nobody checked in by 09:45');
      expect(itemText('Meeting Room A')).not.toContain('Check in at the door');
    });
  });
});
