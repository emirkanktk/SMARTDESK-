import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Dashboard as DashboardData } from '../../core/models/dashboard.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { FakeFavorites, favoritesStub } from '../../../testing/favorites-stub';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { fakeResource } from '../../../testing/resource-fixtures';
import { Dashboard } from './dashboard';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('Dashboard', () => {
  const booking = (id: string, resourceName: string, start: string, end: string, extra = {}) =>
    fakeReservation({
      id,
      resourceName,
      startDateTime: utc(start),
      endDateTime: utc(end),
      ...extra,
    });
  const running = booking('rsv-1', 'Board Room', '2026-10-05T09:30', '2026-10-05T11:00', {
    userId: 'someone-else',
    userDisplayName: 'Mehmet Demir',
    resourceLocation: '5th Floor',
  });
  const overnight = booking('rsv-2', 'Company Car', '2026-10-04T18:00', '2026-10-05T09:00', {
    userId: 'someone-else',
    userDisplayName: null,
  });
  const mineToday = booking('rsv-3', 'Meeting Room A', '2026-10-05T14:00', '2026-10-05T15:30');
  const mineTomorrow = booking('rsv-4', 'Desk 12', '2026-10-06T09:00', '2026-10-06T17:00');

  function dashboard(overrides: Partial<DashboardData> = {}): DashboardData {
    return {
      generatedAt: utc('2026-10-05T10:07'),
      timeZone: 'Europe/Istanbul',
      todayStart: utc('2026-10-05T00:00'),
      todayEnd: utc('2026-10-06T00:00'),
      stats: {
        activeResources: 7,
        resourcesInUseNow: 1,
        reservationsToday: 3,
        myUpcomingReservations: 7,
      },
      today: [overnight, running, mineToday],
      inUseNow: [running],
      myUpcoming: [mineToday, mineTomorrow],
      ...overrides,
    };
  }

  let getDashboard: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let favorites: FakeFavorites;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<Dashboard>;
  let element: HTMLElement;

  beforeEach(async () => {
    getDashboard = vi.fn(() => of(dashboard()));
    getBlocks = vi.fn(() => of([]));
    favorites = new FakeFavorites();
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };

    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [
        provideRouter([]),
        { provide: DashboardService, useValue: { getDashboard } },
        { provide: ReservationHubService, useValue: hub },
        {
          provide: AuthService,
          useValue: { displayName: signal('Ayşe Yılmaz'), currentUser: signal({ id: OWNER_ID }) },
        },
        { provide: ResourceBlockService, useValue: { getBlocks } },
        favoritesStub(favorites),
      ],
    }).compileComponents();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Dashboard);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function section(heading: string): HTMLElement {
    return Array.from(element.querySelectorAll<HTMLElement>('section')).find(
      (s) => s.querySelector('h2')!.textContent!.trim() === heading,
    )!;
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

  function rows(heading: string): string[] {
    return Array.from(section(heading).querySelectorAll('.row')).map(text);
  }

  describe('favourites', () => {
    const board = fakeResource({ id: 'res-board', name: 'Board Room' });
    const camera = fakeResource({ id: 'res-cam', name: 'Camera C-1', status: 'Maintenance' });
    const car = fakeResource({ id: 'res-car', name: 'Company Car' });
    const desk = fakeResource({ id: 'res-desk', name: 'Desk 12' });
    const focus = fakeResource({ id: 'res-focus', name: 'Focus Room' });

    it('says what each is doing now, and books one with a click', async () => {
      favorites.list.set([board, camera, car, desk, focus]);
      getDashboard.mockReturnValue(
        of(
          dashboard({
            today: [
              booking('rsv-b', 'Board Room', '2026-10-05T09:30', '2026-10-05T11:00', {
                resourceId: 'res-board',
              }),
              booking('rsv-d', 'Desk 12', '2026-10-05T14:00', '2026-10-05T15:00', {
                resourceId: 'res-desk',
              }),
            ],
          }),
        ),
      );
      getBlocks.mockReturnValue(
        of([
          {
            id: 'blk-1',
            resourceId: 'res-car',
            resourceName: 'Company Car',
            startDateTime: utc('2026-10-05T10:00'),
            endDateTime: utc('2026-10-05T12:00'),
            reason: 'At the service',
            createdByUserId: 'u-admin',
          },
        ]),
      );
      await render();

      expect(favorites.load).toHaveBeenCalled();
      expect(getBlocks).toHaveBeenCalledWith({
        from: utc('2026-10-05T00:00'),
        to: utc('2026-10-06T00:00'),
      });
      expect(rows('Your favourites')).toEqual([
        'Board Room In use until 11:00 Book',
        'Camera C-1 Not available',
        'Company Car Not available until 12:00 Book',
        'Desk 12 Free until 14:00 Book',
        'Focus Room Free for the rest of the day Book',
      ]);
      const book = Array.from(
        section('Your favourites').querySelectorAll<HTMLAnchorElement>('a.btn'),
      ).map((a) => a.getAttribute('href'));
      expect(book[0]).toBe('/reservations/new?resourceId=res-board');
    });

    it('says how to have some when there are none', async () => {
      await render();

      expect(section('Your favourites').textContent!.replace(/\s+/g, ' ')).toContain(
        'Star what you book often on Resources, and book it from here.',
      );
    });
  });

  it('greets the user with the date of their today', async () => {
    await render();

    expect(element.textContent).toContain('Welcome, Ayşe Yılmaz');
    expect(element.textContent).toContain('Monday 5 October');
  });

  it("asks for today in the browser's time zone", async () => {
    await render();

    expect(getDashboard).toHaveBeenCalledWith(Intl.DateTimeFormat().resolvedOptions().timeZone);
  });

  it('shows the counts, linking to the matching screens', async () => {
    await render();
    const tiles = Array.from(element.querySelectorAll('.tile')).map(text);

    expect(tiles).toEqual([
      '7 Active resources',
      '1 In use now',
      '3 Reservations today',
      '7 Your upcoming',
    ]);
    const links = Array.from(element.querySelectorAll('.tile a')).map((a) =>
      a.getAttribute('href'),
    );
    expect(links).toEqual(['/resources', '/reservations/mine']);
  });

  it("lists today's reservations with who booked them", async () => {
    await render();

    expect(rows('Today')).toEqual([
      'Sun 4 Oct, 18:00-09:00 Company Car 2nd Floor Someone',
      '09:30-11:00 Board Room 5th Floor Now Mehmet Demir',
      '14:00-15:30 Meeting Room A 2nd Floor You',
    ]);
  });

  it('reads an end at midnight as today, and a later one with its date', async () => {
    const lateShift = booking('rsv-7', 'Desk 12', '2026-10-05T22:00', '2026-10-06T00:00');
    const intoTomorrow = booking('rsv-8', 'Board Room', '2026-10-05T09:30', '2026-10-06T01:00', {
      userId: 'someone-else',
      userDisplayName: 'Mehmet Demir',
    });
    getDashboard.mockReturnValue(of(dashboard({ today: [lateShift], inUseNow: [intoTomorrow] })));
    await render();

    expect(rows('Today')).toEqual(['22:00-00:00 Desk 12 2nd Floor You']);
    expect(rows('In use now')).toEqual(['Board Room Mehmet Demir until Tue 6 Oct, 01:00']);
  });

  it('shows what is in use and until when', async () => {
    await render();

    expect(rows('In use now')).toEqual(['Board Room Mehmet Demir until 11:00']);
  });

  it('says which bookings were checked in at the door', async () => {
    const checkedIn = { ...running, checkedInAt: utc('2026-10-05T09:28') };
    getDashboard.mockReturnValue(
      of(dashboard({ today: [overnight, checkedIn, mineToday], inUseNow: [checkedIn] })),
    );
    await render();

    expect(rows('Today')).toEqual([
      'Sun 4 Oct, 18:00-09:00 Company Car 2nd Floor Someone',
      '09:30-11:00 Board Room 5th Floor Now Checked in Mehmet Demir',
      '14:00-15:30 Meeting Room A 2nd Floor You',
    ]);
    expect(rows('In use now')).toEqual(['Board Room Mehmet Demir Checked in until 11:00']);
  });

  it("lists the user's next reservations with a link to all of them", async () => {
    await render();
    const upcoming = section('Your next reservations');

    expect(rows('Your next reservations')).toEqual([
      'Meeting Room A Mon 5 Oct, 14:00 Change',
      'Desk 12 Tue 6 Oct, 09:00 Change',
    ]);
    expect(upcoming.querySelector('.row a')!.getAttribute('href')).toBe('/reservations/rsv-3/edit');
    const seeAll = upcoming.querySelector('.see-all')!;
    expect(seeAll.textContent!.trim()).toBe('See 5 more');
    expect(seeAll.getAttribute('href')).toBe('/reservations/mine');
  });

  it('says so when there is nothing to show', async () => {
    getDashboard.mockReturnValue(
      of(
        dashboard({
          stats: {
            activeResources: 7,
            resourcesInUseNow: 0,
            reservationsToday: 0,
            myUpcomingReservations: 0,
          },
          today: [],
          inUseNow: [],
          myUpcoming: [],
        }),
      ),
    );
    await render();

    expect(element.textContent).toContain('No reservations today.');
    expect(element.textContent).toContain('Nothing is in use right now.');
    expect(element.textContent).toContain('You have no upcoming reservations.');
  });

  it('refetches when a reservation changes anywhere', async () => {
    await render();
    getDashboard.mockReturnValue(of(dashboard({ inUseNow: [] })));
    hub.cancelled$.next({ ...running, status: 'Cancelled' });
    await fixture.whenStable();

    expect(getDashboard).toHaveBeenCalledTimes(2);
    expect(element.textContent).toContain('Nothing is in use right now.');
  });

  it('refetches after the hub reconnects', async () => {
    await render();
    hub.reconnected$.next();
    await fixture.whenStable();

    expect(getDashboard).toHaveBeenCalledTimes(2);
  });

  it('keeps the last overview when a refresh fails', async () => {
    await render();
    getDashboard.mockReturnValue(throwError(() => new ApiError(0, 'Offline')));
    hub.created$.next(mineTomorrow);
    await fixture.whenStable();

    expect(element.textContent).toContain('Board Room');
    expect(element.textContent).not.toContain('could not be loaded');
  });

  it('offers a retry when the first load fails', async () => {
    getDashboard.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain("Today's overview could not be loaded.");
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(element.textContent).toContain('Board Room');
  });
});
