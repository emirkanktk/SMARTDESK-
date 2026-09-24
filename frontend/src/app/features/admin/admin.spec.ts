import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { Admin } from './admin';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('Admin', () => {
  const NOW = new Date('2026-10-05T10:07'); // a Monday, local time

  const room = fakeResource(); // Meeting Room A, Active
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
  });
  const camera = fakeResource({ id: 'res-3', name: 'Camera C-1', status: 'Maintenance' });
  const projector = fakeResource({ id: 'res-4', name: 'Projector P-1', status: 'Inactive' });

  const booking = (id: string, start: string, end: string, extra: Partial<Reservation> = {}) =>
    fakeReservation({ id, startDateTime: utc(start), endDateTime: utc(end), ...extra });
  const theirs = { userId: 'someone-else', userDisplayName: 'Mehmet Demir' };
  const ended = booking('rsv-1', '2026-10-05T08:00', '2026-10-05T09:00', theirs);
  const running = booking('rsv-2', '2026-10-05T09:30', '2026-10-05T11:00', {
    ...theirs,
    note: 'Standup',
  });
  const mine = booking('rsv-3', '2026-10-07T14:00', '2026-10-07T15:00', {
    resourceId: desk.id,
    resourceName: 'Desk 12',
    resourceTypeName: DESK_TYPE.name,
    note: null,
  });

  let getCalendar: ReturnType<typeof vi.fn>;
  let cancel: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<Admin>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getCalendar = vi.fn(() => of([mine, running, ended]));
    cancel = vi.fn((id: string) => of({ ...running, id, status: 'Cancelled' }));
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
      imports: [Admin],
      providers: [
        provideRouter([]),
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([DESK_TYPE, MEETING_ROOM_TYPE]),
            getResources: () => of([room, desk, camera, projector]),
          },
        },
        { provide: ReservationService, useValue: { getCalendar, cancel } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser: signal({ id: OWNER_ID }) } },
        { provide: NotificationService, useValue: { success, error } },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    confirmSpy.mockRestore();
  });

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(Admin);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  /** Visible text with a space between elements, which textContent leaves out. */
  function text(el: Element): string {
    const parts: string[] = [];
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      parts.push(walker.currentNode.textContent!);
    }
    return parts.join(' ').replace(/\s+/g, ' ').trim();
  }

  function rows(): string[] {
    return Array.from(element.querySelectorAll('tbody tr')).map(text);
  }

  async function clickCancel(rowIndex: number): Promise<void> {
    element
      .querySelectorAll('tbody tr')
      [rowIndex].querySelector<HTMLButtonElement>('.cancel')!
      .click();
    await fixture.whenStable();
  }

  it('asks for the week starting today, in UTC', async () => {
    await render();

    expect(getCalendar).toHaveBeenCalledWith({
      from: utc('2026-10-05T00:00'),
      to: utc('2026-10-12T00:00'),
      resourceTypeId: null,
    });
  });

  it('counts resources by status', async () => {
    await render();

    expect(Array.from(element.querySelectorAll('.tile')).map(text)).toEqual([
      '2 Active',
      '1 Inactive',
      '1 Maintenance',
    ]);
  });

  it('lists the reservations earliest first, with who booked them', async () => {
    await render();

    expect(rows()).toEqual([
      'Mon 5 Oct, 08:00-09:00 1 h Meeting Room A Meeting Room · 2nd Floor Mehmet Demir Sprint planning',
      'Mon 5 Oct, 09:30-11:00 1 h 30 min In progress Meeting Room A Meeting Room · 2nd Floor Mehmet Demir Standup Change Cancel',
      'Wed 7 Oct, 14:00-15:00 1 h Desk 12 Desk · 2nd Floor You Change Cancel',
    ]);
    expect(element.querySelectorAll('tbody tr')[2].querySelector('a')!.getAttribute('href')).toBe(
      '/reservations/rsv-3/edit',
    );
  });

  it('searches by person, resource and note on the page', async () => {
    await render();
    const search = element.querySelector<HTMLInputElement>('#search')!;
    search.value = 'standup';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(rows().length).toBe(1);
    expect(text(element.querySelector('.summary')!)).toContain('1 of 3 confirmed reservations');
    expect(getCalendar).toHaveBeenCalledTimes(1);
  });

  it('uses the range from the query string, last day included', async () => {
    await render({ from: '2026-10-01', to: '2026-10-03' });

    expect(getCalendar).toHaveBeenCalledWith(
      expect.objectContaining({ from: utc('2026-10-01T00:00'), to: utc('2026-10-04T00:00') }),
    );
  });

  it.each([
    ['ends before it starts', { from: '2026-10-05', to: '2026-10-04' }, 'The last day is before'],
    ['is too long', { from: '2026-10-01', to: '2026-11-15' }, 'Show at most 31 days'],
  ])('refuses a range that %s, without asking the API', async (_, range, message) => {
    await render(range);

    expect(element.textContent).toContain(message);
    expect(getCalendar).not.toHaveBeenCalled();
  });

  it('keeps the range and type in the query string', async () => {
    await render();

    const from = element.querySelector<HTMLInputElement>('#from')!;
    from.value = '2026-10-08';
    from.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({
        queryParams: { from: '2026-10-08' },
        queryParamsHandling: 'merge',
      }),
    );

    const type = element.querySelector<HTMLSelectElement>('#type')!;
    type.value = DESK_TYPE.id;
    type.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { type: DESK_TYPE.id } }),
    );
  });

  it("cancels someone else's reservation after confirming whose it is", async () => {
    await render();
    await clickCancel(1);

    expect(confirmSpy.mock.calls[0][0]).toBe(
      "Cancel Mehmet Demir's Meeting Room A reservation for Mon 5 Oct, 09:30-11:00?",
    );
    expect(cancel).toHaveBeenCalledWith('rsv-2');
    expect(rows().length).toBe(2);
    expect(success).toHaveBeenCalledWith(
      "Mehmet Demir's Meeting Room A reservation for Mon 5 Oct, 09:30-11:00 was cancelled.",
    );
  });

  it('words it for your own reservation', async () => {
    cancel.mockReturnValue(of({ ...mine, status: 'Cancelled' }));
    await render();
    await clickCancel(2);

    expect(confirmSpy.mock.calls[0][0]).toBe(
      'Cancel your Desk 12 reservation for Wed 7 Oct, 14:00-15:00?',
    );
    expect(success).toHaveBeenCalledWith(
      'Your Desk 12 reservation for Wed 7 Oct, 14:00-15:00 was cancelled.',
    );
  });

  it('does nothing when the confirmation is declined', async () => {
    confirmSpy.mockReturnValue(false);
    await render();
    await clickCancel(1);

    expect(cancel).not.toHaveBeenCalled();
  });

  it('shows the API message when it can no longer be cancelled, and refreshes', async () => {
    cancel.mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', 'This reservation has already ended.')),
    );
    await render();
    await clickCancel(1);

    expect(error).toHaveBeenCalledWith('This reservation has already ended.');
    expect(getCalendar).toHaveBeenCalledTimes(2);
  });

  it('offers a retry when loading fails', async () => {
    getCalendar.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain('Reservations could not be loaded.');
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(rows().length).toBe(3);
  });

  describe('live updates', () => {
    it('adds a booking in the range and drops a cancelled one', async () => {
      await render();
      hub.created$.next(booking('rsv-9', '2026-10-06T12:00', '2026-10-06T13:00', theirs));
      hub.cancelled$.next({ ...running, status: 'Cancelled' });
      await fixture.whenStable();

      expect(rows().map((r) => r.slice(0, 22))).toEqual([
        'Mon 5 Oct, 08:00-09:00',
        'Tue 6 Oct, 12:00-13:00',
        'Wed 7 Oct, 14:00-15:00',
      ]);
    });

    it('ignores a booking of another type while filtering by type', async () => {
      getCalendar.mockReturnValue(of([mine]));
      await render({ type: DESK_TYPE.id });
      hub.created$.next(booking('rsv-9', '2026-10-06T12:00', '2026-10-06T13:00', theirs)); // a room

      expect(getCalendar).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeId: DESK_TYPE.id }),
      );
      await fixture.whenStable();
      expect(rows().length).toBe(1);
    });

    it('keeps a change that arrives while the list is loading', async () => {
      const response = new Subject<Reservation[]>();
      getCalendar.mockReturnValue(response);
      await render();

      hub.created$.next(booking('rsv-9', '2026-10-06T12:00', '2026-10-06T13:00', theirs));
      response.next([mine, running, ended]); // queried before rsv-9 was committed
      response.complete();
      await fixture.whenStable();

      expect(rows().length).toBe(4);
    });

    it('refetches after the connection comes back', async () => {
      await render();
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(getCalendar).toHaveBeenCalledTimes(2);
    });
  });
});
