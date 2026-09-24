import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { MyReservations } from './my-reservations';

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
        { provide: ReservationService, useValue: { getMine, cancel } },
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

  it('shows both dates for a reservation that spans days', async () => {
    await render();
    const card = element.querySelectorAll('.item')[2].textContent!.replace(/\s+/g, ' ');

    expect(card).toContain('09:00 to Fri 9 Oct, 09:00');
    expect(card).toContain('1 day');
  });

  it('counts each tab', async () => {
    await render();

    expect(tabText('Upcoming')).toBe('Upcoming 3');
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

    it('refetches after the connection comes back', async () => {
      await render();
      getMine.mockReturnValue(of([soon]));
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(getMine).toHaveBeenCalledTimes(2);
      expect(names()).toEqual(['Meeting Room A']);
    });
  });
});
