import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { OfficePerson, OfficeVisit, WhosIn as WhosInDay } from '../../core/models/whos-in.model';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { WhosInService } from '../../core/services/whos-in.service';
import { WhosIn, initialsOf } from './whos-in';

describe('WhosIn', () => {
  /** A time on 26 November 2026, on the browser's own clock, whatever its zone. */
  const at = (time: string, day = '2026-11-26') => new Date(`${day}T${time}:00`);

  const visit = (
    id: string,
    resourceName: string,
    location: string,
    from: string,
    to: string,
    extra: Partial<OfficeVisit> = {},
  ): OfficeVisit => ({
    reservationId: id,
    resourceId: `res-${id}`,
    resourceName,
    resourceTypeName: 'Desk',
    location,
    startDateTime: at(from).toISOString(),
    endDateTime: at(to).toISOString(),
    checkedIn: false,
    invited: false,
    organizerName: null,
    ...extra,
  });

  const ayse: OfficePerson = {
    userId: 'u-ayse',
    name: 'Ayşe Yılmaz',
    isMe: true,
    inMyTeam: true,
    visits: [visit('r-desk', 'Desk 3-05', '3rd Floor', '09:00', '17:00', { checkedIn: true })],
  };
  const mehmet: OfficePerson = {
    userId: 'u-mehmet',
    name: 'Mehmet Demir',
    isMe: false,
    inMyTeam: true,
    visits: [
      visit('r-room', 'Board Room', '5th Floor', '14:00', '15:00', {
        invited: true,
        organizerName: 'Zeynep Kaya',
      }),
    ],
  };
  const okan: OfficePerson = {
    userId: 'u-okan',
    name: 'Okan Arslan',
    isMe: false,
    inMyTeam: false,
    visits: [visit('r-desk2', 'Desk 2-01', '2nd Floor', '08:00', '09:30')],
  };
  const today: WhosInDay = {
    date: '2026-11-26',
    timeZone: 'Europe/Istanbul',
    hasTeam: true,
    people: [ayse, mehmet, okan],
  };

  let get: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<WhosIn>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(at('10:05'));
    get = vi.fn(() => of(today));
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: WhosInService, useValue: { get } },
        { provide: ReservationHubService, useValue: hub },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  async function render(date?: string): Promise<void> {
    fixture = TestBed.createComponent(WhosIn);
    if (date) {
      fixture.componentRef.setInput('date', date);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (node: Element) => node.textContent!.replace(/\s+/g, ' ').trim();
  const names = () => [...element.querySelectorAll('.person .name')].map(text);
  const button = (label: string) =>
    [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => text(b) === label || b.getAttribute('aria-label') === label,
    )!;
  async function click(target: HTMLElement): Promise<void> {
    target.click();
    await fixture.whenStable();
  }
  async function search(value: string): Promise<void> {
    const box = element.querySelector<HTMLInputElement>('input[type="search"]')!;
    box.value = value;
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  it('lists who is in today, where and when, and how many of them are in the team', async () => {
    await render();

    expect(get).toHaveBeenCalledWith('2026-11-26');
    expect(text(element.querySelector('.summary')!)).toBe(
      '3 people are in the office on Thursday 26 November. 2 of them from your team.',
    );
    expect(names()).toEqual(['Ayşe Yılmaz You', 'Mehmet Demir Your team', 'Okan Arslan']);
    expect([...element.querySelectorAll('.avatar')].map(text)).toEqual(['AY', 'MD', 'OA']);
    // Each part of a visit is a box of its own, set apart by the row's gap.
    const visits = [...element.querySelectorAll('.visit')].map((row) =>
      [...row.children].map(text).join(' '),
    );
    expect(visits).toEqual([
      'Desk 3-05 3rd Floor 09:00–17:00 Now Checked in',
      'Board Room 5th Floor 14:00–15:00 Meeting organized by Zeynep Kaya',
      'Desk 2-01 2nd Floor 08:00–09:30',
    ]);
  });

  it('opens the floor plan at a place: now while it is under way, else at its time', async () => {
    await render();

    const links = [...element.querySelectorAll<HTMLAnchorElement>('a.place')];
    const params = (link: HTMLAnchorElement) =>
      new URL(link.getAttribute('href')!, 'http://app').searchParams;
    expect(links[0].getAttribute('href')!.startsWith('/floor-plan?')).toBe(true);
    expect(params(links[0]).get('floor')).toBe('3rd Floor');
    expect(params(links[0]).has('at')).toBe(false);
    expect(params(links[1]).get('floor')).toBe('5th Floor');
    expect(params(links[1]).get('at')).toBe(at('14:00').toISOString());
  });

  it('narrows the list to the team, and to a name', async () => {
    await render();

    await click(button('My team'));
    expect(names()).toEqual(['Ayşe Yılmaz You', 'Mehmet Demir Your team']);
    expect(button('My team').getAttribute('aria-pressed')).toBe('true');

    await search('MEH');
    expect(names()).toEqual(['Mehmet Demir Your team']);

    await search('zeynep');
    expect(text(element.querySelector('.empty-note')!)).toBe('Nobody in that day matches.');

    await search('');
    await click(button('Everyone'));
    expect(names()).toHaveLength(3);
  });

  it('says when nobody from the team is in, and when nobody is in at all', async () => {
    get.mockReturnValueOnce(of({ ...today, people: [okan] }));
    await render();

    await click(button('My team'));
    expect(text(element.querySelector('.empty-note')!)).toBe(
      'Nobody from your team is in that day.',
    );

    get.mockReturnValueOnce(of({ ...today, people: [] }));
    fixture.componentRef.setInput('date', '2026-11-30');
    await fixture.whenStable();
    expect(text(element.querySelector('.summary')!)).toBe(
      'Nobody is in the office on Monday 30 November.',
    );
  });

  it('without a team, has nothing to narrow it to', async () => {
    get.mockReturnValueOnce(
      of({ ...today, hasTeam: false, people: [{ ...okan }, { ...mehmet, inMyTeam: false }] }),
    );
    await render();

    expect(element.querySelector('.show-switch')).toBeNull();
    expect(text(element.querySelector('.summary')!)).toBe(
      '2 people are in the office on Thursday 26 November.',
    );
  });

  it('goes a day at a time, and back to today without a date in the address', async () => {
    await render();
    expect(button('Today')).toBeUndefined();

    await click(button('Next day'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { date: '2026-11-27' } }),
    );

    fixture.componentRef.setInput('date', '2026-11-27');
    await fixture.whenStable();
    expect(get).toHaveBeenLastCalledWith('2026-11-27');
    expect(element.querySelector('.person')).not.toBeNull();

    await click(button('Previous day'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { date: null } }),
    );
    await click(button('Today'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { date: null } }),
    );
  });

  it('asks again when a booking changes, once for a burst, and after a lost connection', async () => {
    await render();
    expect(get).toHaveBeenCalledTimes(1);

    hub.created$.next({} as Reservation);
    hub.updated$.next({} as Reservation);
    vi.advanceTimersByTime(400);
    await fixture.whenStable();
    expect(get).toHaveBeenCalledTimes(2);

    hub.reconnected$.next();
    await fixture.whenStable();
    expect(get).toHaveBeenCalledTimes(3);
    expect(names()).toHaveLength(3);
  });

  it('says when it could not load, and tries again', async () => {
    get.mockReturnValueOnce(throwError(() => new ApiError(0, 'Offline')));
    await render();

    expect(text(element.querySelector('[role="alert"]')!)).toContain(
      "Who's in could not be loaded.",
    );
    await click(button('Try again'));

    expect(names()).toHaveLength(3);
  });
});

describe('initialsOf', () => {
  it('takes the first and last names, in the language capitals', () => {
    expect(initialsOf('Ayşe Yılmaz', 'tr')).toBe('AY');
    expect(initialsOf('ilker  can  demir', 'tr')).toBe('İD');
    expect(initialsOf('Cher', 'en')).toBe('C');
    expect(initialsOf('  ', 'en')).toBe('?');
  });
});
