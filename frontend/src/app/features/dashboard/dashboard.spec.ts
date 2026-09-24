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
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
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

  it('shows what is in use and until when', async () => {
    await render();

    expect(rows('In use now')).toEqual(['Board Room Mehmet Demir until 11:00']);
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
