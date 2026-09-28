import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { AuthService } from '../../core/services/auth.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { Calendar } from './calendar';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('Calendar', () => {
  const NOW = new Date('2026-10-07T10:07'); // a Wednesday, local time
  const CAMERA_TYPE = { id: 'type-camera', name: 'Camera' };

  const room = fakeResource(); // Meeting Room A
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    location: '3rd Floor',
  });
  const camera = fakeResource({
    id: 'res-3',
    name: 'Camera C-1',
    resourceTypeId: CAMERA_TYPE.id,
    resourceTypeName: CAMERA_TYPE.name,
    status: 'Maintenance',
  });

  const booking = (id: string, resourceId: string, start: string, end: string, extra = {}) =>
    fakeReservation({
      id,
      resourceId,
      startDateTime: utc(start),
      endDateTime: utc(end),
      ...extra,
    });
  const mine = booking('rsv-1', room.id, '2026-10-06T10:00', '2026-10-06T11:30');
  const theirs = booking('rsv-2', desk.id, '2026-10-07T09:00', '2026-10-07T17:00', {
    resourceName: desk.name,
    userId: 'someone-else',
    userDisplayName: 'Mehmet Demir',
  });
  const overnight = booking('rsv-3', room.id, '2026-10-08T22:00', '2026-10-09T02:00', {
    userId: 'someone-else',
    userDisplayName: null,
  });

  let getResources: ReturnType<typeof vi.fn>;
  let getCalendar: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<Calendar>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getResources = vi.fn(() => of([room, desk, camera]));
    getCalendar = vi.fn(() => of([mine, theirs, overnight]));
    getBlocks = vi.fn(() => of([]));
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };

    await TestBed.configureTestingModule({
      imports: [Calendar],
      providers: [
        provideRouter([]),
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([CAMERA_TYPE, DESK_TYPE, MEETING_ROOM_TYPE]),
            getResources,
          },
        },
        { provide: ReservationService, useValue: { getCalendar } },
        { provide: ResourceBlockService, useValue: { getBlocks } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser: signal({ id: OWNER_ID }) } },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => vi.useRealTimers());

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(Calendar);
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

  function summary(): string {
    return text(element.querySelector('.summary')!);
  }

  function rowNames(): string[] {
    return Array.from(element.querySelectorAll('tbody th .name')).map((n) => n.textContent!);
  }

  /** The cell for a resource on a day of the week, Monday = 0. */
  function cell(resourceName: string, day: number): HTMLTableCellElement {
    const row = Array.from(element.querySelectorAll('tbody tr')).find(
      (r) => r.querySelector('.name')!.textContent === resourceName,
    )!;
    return row.querySelectorAll('td')[day];
  }

  function entries(resourceName: string, day: number): string[] {
    return Array.from(cell(resourceName, day).querySelectorAll('.entry')).map(text);
  }

  function bookParams(resourceName: string, day: number): URLSearchParams | null {
    const link = cell(resourceName, day).querySelector<HTMLAnchorElement>('.book');
    return link ? new URL(link.href).searchParams : null;
  }

  it('asks for Monday to Monday of the current week, in UTC', async () => {
    await render();

    expect(getCalendar).toHaveBeenCalledWith({
      from: utc('2026-10-05T00:00'),
      to: utc('2026-10-12T00:00'),
      resourceTypeId: null,
    });
    expect(getResources).toHaveBeenCalledWith({ resourceTypeId: null });
  });

  it('shows the week with today marked', async () => {
    await render();
    const headers = Array.from(element.querySelectorAll('thead th')).map(text);

    expect(element.querySelector('.week-label')!.textContent).toBe('5 - 11 Oct 2026');
    expect(headers).toEqual([
      'Resource',
      'Mon 5 Oct',
      'Tue 6 Oct',
      'Wed 7 Oct',
      'Thu 8 Oct',
      'Fri 9 Oct',
      'Sat 10 Oct',
      'Sun 11 Oct',
    ]);
    expect(element.querySelectorAll('thead th')[3].classList).toContain('today');
    const thisWeek = Array.from(
      element.querySelectorAll<HTMLButtonElement>('.week-nav button'),
    ).find((b) => text(b) === 'This week')!;
    expect(thisWeek.disabled).toBe(true);
  });

  it('lists every resource, by type and then name, flagging the unavailable ones', async () => {
    await render();

    expect(rowNames()).toEqual(['Camera C-1', 'Desk 12', 'Meeting Room A']);
    expect(text(element.querySelector('tbody tr')!.querySelector('th')!)).toContain('Maintenance');
  });

  it('shows who booked what, with your own marked and linked to the reservation', async () => {
    await render();

    expect(entries('Meeting Room A', 1)).toEqual(['10:00-11:30 You']);
    expect(entries('Desk 12', 2)).toEqual(['09:00-17:00 Mehmet Demir']);
    const link = cell('Meeting Room A', 1).querySelector('.entry')!;
    expect(link.classList).toContain('mine');
    expect(link.getAttribute('href')).toBe('/reservations/rsv-1/edit');
  });

  it('splits a reservation over the days it spans', async () => {
    await render();

    expect(entries('Meeting Room A', 3)).toEqual(['from 22:00 Someone']);
    expect(entries('Meeting Room A', 4)).toEqual(['until 02:00 Someone']);
  });

  it('offers booking only on bookable days: next half hour today, 09:00 later', async () => {
    await render();

    expect(bookParams('Meeting Room A', 1)).toBeNull(); // Tuesday is past
    expect(bookParams('Camera C-1', 3)).toBeNull(); // in maintenance
    const today = bookParams('Meeting Room A', 2)!;
    expect(today.get('resourceId')).toBe(room.id);
    expect(today.get('start')).toBe(utc('2026-10-07T10:30'));
    expect(today.get('end')).toBe(utc('2026-10-07T11:30'));
    expect(bookParams('Desk 12', 3)!.get('start')).toBe(utc('2026-10-08T09:00'));
  });

  it('shows the week from the query string, across a month boundary', async () => {
    await render({ week: '2026-09-30' });

    expect(element.querySelector('.week-label')!.textContent).toBe('28 Sep - 4 Oct 2026');
    expect(getCalendar).toHaveBeenCalledWith(
      expect.objectContaining({ from: utc('2026-09-28T00:00'), to: utc('2026-10-05T00:00') }),
    );
  });

  it('moves between weeks and filters by type through the query string', async () => {
    await render();

    element.querySelector<HTMLButtonElement>('[aria-label="Next week"]')!.click();
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({
        queryParams: { week: '2026-10-12' },
        queryParamsHandling: 'merge',
      }),
    );

    const select = element.querySelector<HTMLSelectElement>('#type')!;
    select.value = DESK_TYPE.id;
    select.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { type: DESK_TYPE.id } }),
    );
  });

  it('passes the type filter to both requests', async () => {
    await render({ type: DESK_TYPE.id });

    expect(getResources).toHaveBeenCalledWith({ resourceTypeId: DESK_TYPE.id });
    expect(getCalendar).toHaveBeenCalledWith(
      expect.objectContaining({ resourceTypeId: DESK_TYPE.id }),
    );
  });

  it('says so when a type has no resources', async () => {
    getResources.mockReturnValue(of([]));
    getCalendar.mockReturnValue(of([]));
    await render({ type: CAMERA_TYPE.id });

    expect(element.textContent).toContain('No resources of this type.');
  });

  it('offers a retry when loading fails', async () => {
    getCalendar.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain('The calendar could not be loaded.');
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(entries('Meeting Room A', 1)).toEqual(['10:00-11:30 You']);
  });

  describe('live updates', () => {
    it('adds a booking in this week', async () => {
      await render();
      hub.created$.next(booking('rsv-9', desk.id, '2026-10-09T13:00', '2026-10-09T14:00'));
      await fixture.whenStable();

      expect(entries('Desk 12', 4)).toEqual(['13:00-14:00 You']);
    });

    it('ignores a booking in another week', async () => {
      await render();
      hub.created$.next(booking('rsv-9', desk.id, '2026-10-16T13:00', '2026-10-16T14:00'));
      await fixture.whenStable();

      expect(element.querySelectorAll('.entry').length).toBe(4);
      expect(summary()).toContain('3 reservations this week');
    });

    it('moves a changed reservation, and drops one moved out of the week', async () => {
      await render();
      hub.updated$.next({
        ...mine,
        startDateTime: utc('2026-10-08T10:00'),
        endDateTime: utc('2026-10-08T11:00'),
      });
      await fixture.whenStable();

      expect(entries('Meeting Room A', 1)).toEqual([]);
      expect(entries('Meeting Room A', 3)).toEqual(['10:00-11:00 You', 'from 22:00 Someone']);

      hub.updated$.next({
        ...mine,
        startDateTime: utc('2026-10-20T10:00'),
        endDateTime: utc('2026-10-20T11:00'),
      });
      await fixture.whenStable();
      expect(entries('Meeting Room A', 3)).toEqual(['from 22:00 Someone']);
      expect(summary()).toContain('2 reservations this week');
    });

    it('drops a cancelled reservation', async () => {
      await render();
      hub.cancelled$.next({ ...theirs, status: 'Cancelled' });
      await fixture.whenStable();

      expect(entries('Desk 12', 2)).toEqual([]);
    });

    it('keeps changes that arrive while the week is still loading', async () => {
      const response = new Subject<Reservation[]>();
      getCalendar.mockReturnValue(response);
      await render();

      hub.created$.next(booking('rsv-9', desk.id, '2026-10-09T13:00', '2026-10-09T14:00'));
      hub.cancelled$.next({ ...theirs, status: 'Cancelled' });
      // The server ran the query before either change was committed.
      response.next([mine, theirs, overnight]);
      response.complete();
      await fixture.whenStable();

      expect(entries('Desk 12', 4)).toEqual(['13:00-14:00 You']);
      expect(entries('Desk 12', 2)).toEqual([]);
    });

    it('refetches after the connection comes back', async () => {
      await render();
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(getCalendar).toHaveBeenCalledTimes(2);
    });
  });
  describe('blocked windows', () => {
    const projector: ResourceBlock = {
      id: 'blk-1',
      resourceId: room.id,
      resourceName: room.name,
      startDateTime: utc('2026-10-06T14:00'),
      endDateTime: utc('2026-10-07T12:00'),
      reason: 'Projector replacement',
      createdByUserId: 'admin-1',
    };

    function blocked(resourceName: string, day: number): string[] {
      return Array.from(cell(resourceName, day).querySelectorAll('.blocked')).map(text);
    }

    it('asks for the blocks of the week shown', async () => {
      await render();

      expect(getBlocks).toHaveBeenCalledWith({
        from: utc('2026-10-05T00:00'),
        to: utc('2026-10-12T00:00'),
      });
    });

    it('draws a block in each day it covers, with its reason', async () => {
      getBlocks.mockReturnValue(of([projector]));
      await render();

      expect(blocked('Meeting Room A', 1)).toEqual(['from 14:00 Blocked: Projector replacement']);
      expect(blocked('Meeting Room A', 2)).toEqual(['until 12:00 Blocked: Projector replacement']);
      expect(blocked('Meeting Room A', 3)).toEqual([]);
      expect(cell('Meeting Room A', 1).querySelector('.blocked')!.getAttribute('title')).toBe(
        'Meeting Room A is blocked Tue 6 Oct, 14:00 to Wed 7 Oct, 12:00: Projector replacement',
      );
      expect(summary()).toContain('1 blocked window.');
    });

    it('leaves out blocks of resources the type filter hides', async () => {
      getResources.mockReturnValue(of([desk]));
      getCalendar.mockReturnValue(of([theirs]));
      getBlocks.mockReturnValue(of([projector]));
      await render({ type: DESK_TYPE.id });

      expect(element.querySelector('.blocked')).toBeNull();
      expect(summary()).not.toContain('blocked');
    });

    it('still shows the calendar when the blocks cannot be loaded', async () => {
      getBlocks.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render();

      expect(rowNames().length).toBe(3);
      expect(summary()).toContain('Blocked windows could not be loaded.');
    });

    it('fetches the blocks again after the connection comes back', async () => {
      await render();
      hub.reconnected$.next();
      await fixture.whenStable();

      expect(getBlocks).toHaveBeenCalledTimes(2);
    });
  });

  describe('month view', () => {
    /** The cell of a day of the month, by its label, e.g. "Tue 6 Oct". */
    function day(label: string): HTMLTableCellElement {
      return element
        .querySelector(`a.day-number[aria-label="Week of ${label}"]`)!
        .closest('td') as HTMLTableCellElement;
    }

    function dayEntries(label: string): string[] {
      return Array.from(day(label).querySelectorAll('.entry')).map(text);
    }

    it("asks for the month's own days, in UTC", async () => {
      await render({ view: 'month' });

      const range = { from: utc('2026-10-01T00:00'), to: utc('2026-11-01T00:00') };
      expect(getCalendar).toHaveBeenCalledWith({ ...range, resourceTypeId: null });
      expect(getBlocks).toHaveBeenCalledWith(range);
      expect(element.querySelector('.week-label')!.textContent).toBe('October 2026');
      expect(summary()).toContain('3 reservations this month.');
    });

    it('lays out Monday-first weeks, with the days of the neighbouring months empty', async () => {
      await render({ view: 'month' });

      const headers = Array.from(element.querySelectorAll('.month thead th')).map(text);
      expect(headers).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']);
      const weeks = element.querySelectorAll('.month tbody tr');
      expect(weeks.length).toBe(5); // 28 Sep to 1 Nov
      const first = weeks[0].querySelector('td')!;
      expect(first.classList).toContain('outside');
      expect(text(first)).toBe('28');
      expect(day('Wed 7 Oct').classList).toContain('today');
      expect(day('Tue 6 Oct').classList).toContain('past');
    });

    it('lists each day with resource, time and who, blocks included', async () => {
      getBlocks.mockReturnValue(
        of([
          {
            id: 'blk-1',
            resourceId: desk.id,
            resourceName: desk.name,
            startDateTime: utc('2026-10-08T22:30'),
            endDateTime: utc('2026-10-08T23:30'),
            reason: 'Painting',
            createdByUserId: 'admin-1',
          },
        ]),
      );
      await render({ view: 'month' });

      expect(dayEntries('Tue 6 Oct')).toEqual(['10:00-11:30 Meeting Room A · You']);
      expect(dayEntries('Wed 7 Oct')).toEqual(['09:00-17:00 Desk 12 · Mehmet Demir']);
      expect(dayEntries('Thu 8 Oct')).toEqual([
        'from 22:00 Meeting Room A · Someone',
        '22:30-23:30 Desk 12 · Blocked',
      ]);
      expect(dayEntries('Fri 9 Oct')).toEqual(['until 02:00 Meeting Room A · Someone']);
      expect(day('Tue 6 Oct').querySelector('a.entry')!.getAttribute('href')).toBe(
        '/reservations/rsv-1/edit',
      );
    });

    it('shows three entries a day and links the rest, and the day, to its week', async () => {
      getCalendar.mockReturnValue(
        of(
          ['09', '10', '11', '12', '13'].map((hour) =>
            booking(`rsv-${hour}`, room.id, `2026-10-14T${hour}:00`, `2026-10-14T${hour}:30`),
          ),
        ),
      );
      await render({ view: 'month' });

      expect(dayEntries('Wed 14 Oct').length).toBe(3);
      const more = day('Wed 14 Oct').querySelector<HTMLAnchorElement>('.more')!;
      expect(text(more)).toBe('+2 more');
      expect(more.getAttribute('href')).toContain('week=2026-10-14');
      expect(day('Wed 14 Oct').querySelector('.day-number')!.getAttribute('href')).toContain(
        'week=2026-10-14',
      );
    });

    it('moves between months through the query string', async () => {
      await render({ view: 'month' });
      expect(
        Array.from(element.querySelectorAll<HTMLButtonElement>('.week-nav button')).find(
          (b) => text(b) === 'This month',
        )!.disabled,
      ).toBe(true);

      element.querySelector<HTMLButtonElement>('[aria-label="Next month"]')!.click();
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { month: '2026-11' } }),
      );
      element.querySelector<HTMLButtonElement>('[aria-label="Previous month"]')!.click();
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { month: '2026-09' } }),
      );
    });

    it('switches views, keeping to the same weeks', async () => {
      const button = (label: string) =>
        Array.from(element.querySelectorAll<HTMLButtonElement>('.view-switch button')).find(
          (b) => text(b) === label,
        )!;

      await render({ week: '2026-11-18' });
      expect(button('Week').getAttribute('aria-pressed')).toBe('true');
      button('Month').click();
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { view: 'month', month: '2026-11', week: null } }),
      );

      fixture.componentRef.setInput('view', 'month');
      fixture.componentRef.setInput('month', '2026-12');
      await fixture.whenStable();
      button('Week').click();
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { view: null, week: '2026-12-01', month: null } }),
      );
    });

    it('adds a live booking in the month, and ignores one in another month', async () => {
      await render({ view: 'month' });

      const deskBooking = { resourceName: desk.name };
      hub.created$.next(
        booking('rsv-9', desk.id, '2026-10-20T13:00', '2026-10-20T14:00', deskBooking),
      );
      hub.created$.next(
        booking('rsv-10', desk.id, '2026-11-03T13:00', '2026-11-03T14:00', deskBooking),
      );
      await fixture.whenStable();

      expect(dayEntries('Tue 20 Oct')).toEqual(['13:00-14:00 Desk 12 · You']);
      expect(summary()).toContain('4 reservations this month.');
    });
  });
});
