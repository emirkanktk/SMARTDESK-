import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { UsageReport as UsageReportData } from '../../core/models/report.model';
import { browserTimeZone } from '../../core/services/dashboard.service';
import { ReportService } from '../../core/services/report.service';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, MEETING_ROOM_TYPE } from '../../../testing/resource-fixtures';
import { UsageReport } from './usage-report';

describe('UsageReport', () => {
  const NOW = new Date('2026-10-05T10:07'); // a Monday, local time

  function report(overrides: Partial<UsageReportData> = {}): UsageReportData {
    return {
      from: '2026-09-06',
      to: '2026-10-05',
      timeZone: 'Europe/Istanbul',
      fromUtc: '2026-09-05T21:00:00Z',
      toUtc: '2026-10-05T21:00:00Z',
      workingHours: 'Mon-Fri 09:00-18:00',
      workingHoursPerResource: 198,
      totals: {
        resources: 3,
        reservations: 14,
        cancelled: 2,
        bookedHours: 120.25,
        workingHoursBooked: 98,
        workingHoursAvailable: 594,
        utilization: 0.165,
      },
      resources: [
        {
          resourceId: 'res-1',
          resourceName: 'Meeting Room A',
          resourceTypeId: MEETING_ROOM_TYPE.id,
          resourceTypeName: MEETING_ROOM_TYPE.name,
          status: 'Active',
          reservations: 10,
          cancelled: 1,
          bookedHours: 70,
          workingHoursBooked: 66,
          utilization: 0.3333,
        },
        {
          resourceId: 'res-3',
          resourceName: 'Camera C-1',
          resourceTypeId: 'type-camera',
          resourceTypeName: 'Camera',
          status: 'Maintenance',
          reservations: 4,
          cancelled: 1,
          bookedHours: 50.25,
          workingHoursBooked: 32,
          utilization: 0.1616,
        },
        {
          resourceId: 'res-2',
          resourceName: 'Desk 12',
          resourceTypeId: DESK_TYPE.id,
          resourceTypeName: DESK_TYPE.name,
          status: 'Active',
          reservations: 0,
          cancelled: 0,
          bookedHours: 0,
          workingHoursBooked: 0,
          utilization: 0,
        },
      ],
      byType: [
        {
          resourceTypeId: MEETING_ROOM_TYPE.id,
          resourceTypeName: MEETING_ROOM_TYPE.name,
          resources: 1,
          reservations: 10,
          cancelled: 1,
          bookedHours: 70,
          workingHoursBooked: 66,
          workingHoursAvailable: 198,
          utilization: 0.3333,
        },
      ],
      topUsers: [
        { userId: 'u-1', displayName: 'Ayşe Yılmaz', reservations: 9, bookedHours: 60.5 },
        { userId: 'u-2', displayName: null, reservations: 1, bookedHours: 2 },
      ],
      ...overrides,
    };
  }

  let getUsage: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<UsageReport>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getUsage = vi.fn(() => of(report()));

    await TestBed.configureTestingModule({
      imports: [UsageReport],
      providers: [
        provideRouter([]),
        { provide: ReportService, useValue: { getUsage } },
        {
          provide: ResourceService,
          useValue: { getResourceTypes: () => of([DESK_TYPE, MEETING_ROOM_TYPE]) },
        },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => vi.useRealTimers());

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(UsageReport);
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

  function rows(table: string): string[] {
    return Array.from(element.querySelectorAll(`table.${table} tbody tr`)).map(text);
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(element.querySelectorAll('button')).find((b) => text(b) === label)!;
  }

  function lastNavigation(): Record<string, unknown> {
    return navigate.mock.lastCall![1].queryParams;
  }

  it('asks for the last 30 days, today included, in the browser time zone', async () => {
    await render();

    expect(getUsage).toHaveBeenCalledExactlyOnceWith({
      from: '2026-09-06',
      to: '2026-10-05',
      timeZone: browserTimeZone(),
      resourceTypeId: null,
    });
  });

  it('uses the range and type from the query string', async () => {
    await render({ from: '2026-09-01', to: '2026-09-30', type: DESK_TYPE.id });

    expect(getUsage).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        from: '2026-09-01',
        to: '2026-09-30',
        resourceTypeId: DESK_TYPE.id,
      }),
    );
    expect(element.querySelector<HTMLInputElement>('#from')!.value).toBe('2026-09-01');
    expect(element.querySelector<HTMLSelectElement>('#type')!.value).toBe(DESK_TYPE.id);
  });

  it('shows the totals as tiles', async () => {
    await render();

    const tiles = Array.from(element.querySelectorAll('.tile')).map(text);
    expect(tiles).toEqual([
      '16.5% of office hours booked',
      '120.3 h booked in total',
      '14 reservations',
      '2 cancelled',
    ]);
    expect(text(element.querySelector('.note')!)).toContain('198 h per resource');
  });

  it('lists resources busiest first, flags ones that are not active, and draws the bars', async () => {
    await render();

    expect(rows('resources')).toEqual([
      'Meeting Room A Meeting Room 33.3% 70 h 66 h 10 1',
      'Camera C-1 Camera Maintenance 16.2% 50.3 h 32 h 4 1',
      'Desk 12 Desk 0% 0 h 0 h 0 0',
    ]);
    const bars = Array.from(element.querySelectorAll<HTMLElement>('table.resources .fill'));
    expect(bars.map((b) => b.style.width)).toEqual(['33.33%', '16.16%', '0%']);
  });

  it('lists types and the people who booked most, naming unknown ones', async () => {
    await render();

    expect(rows('types')).toEqual(['Meeting Room 33.3% 70 h 1 10']);
    const people = Array.from(element.querySelectorAll('.people li')).map(text);
    expect(people).toEqual([
      'Ayşe Yılmaz 60.5 h · 9 reservations',
      'Unknown user 2 h · 1 reservation',
    ]);
  });

  it('says so when there is nothing to report', async () => {
    getUsage.mockReturnValue(of(report({ resources: [], byType: [], topUsers: [] })));

    await render();

    expect(text(element)).toContain('No resources to report on for this range.');
    expect(text(element)).toContain('Nobody booked anything in this range.');
    expect(element.querySelector('table')).toBeNull();
  });

  it('refuses a range that ends before it starts, without asking the API', async () => {
    await render({ from: '2026-10-05', to: '2026-10-01' });

    expect(getUsage).not.toHaveBeenCalled();
    expect(text(element.querySelector('[role="alert"]')!)).toBe(
      'The last day is before the first day.',
    );
  });

  it('allows 92 days but not 93', async () => {
    await render({ from: '2026-10-01', to: '2026-12-31' }); // 92 days
    expect(getUsage).toHaveBeenCalledTimes(1);

    getUsage.mockClear();
    await render({ from: '2026-10-01', to: '2027-01-01' }); // 93 days
    expect(getUsage).not.toHaveBeenCalled();
    expect(text(element.querySelector('[role="alert"]')!)).toBe('Show at most 92 days at a time.');
  });

  it('counts calendar days across the end of daylight saving', async () => {
    // 25 October 2026 is 25 hours long in Europe; 92 calendar days must still pass.
    await render({ from: '2026-09-01', to: '2026-12-01' });

    expect(getUsage).toHaveBeenCalledTimes(1);
  });

  it('puts the quick ranges in the query string', async () => {
    await render();

    button('Last 7 days').click();
    expect(lastNavigation()).toEqual({ from: '2026-09-29', to: '2026-10-05' });
    button('This month').click();
    expect(lastNavigation()).toEqual({ from: '2026-10-01', to: '2026-10-05' });
    button('Last month').click();
    expect(lastNavigation()).toEqual({ from: '2026-09-01', to: '2026-09-30' });
    button('Last 30 days').click();
    expect(lastNavigation()).toEqual({ from: '2026-09-06', to: '2026-10-05' });
  });

  it('puts picked dates and the type in the query string', async () => {
    await render();

    const from = element.querySelector<HTMLInputElement>('#from')!;
    from.value = '2026-08-01';
    from.dispatchEvent(new Event('change'));
    expect(lastNavigation()).toEqual({ from: '2026-08-01' });

    const type = element.querySelector<HTMLSelectElement>('#type')!;
    type.value = MEETING_ROOM_TYPE.id;
    type.dispatchEvent(new Event('change'));
    expect(lastNavigation()).toEqual({ type: MEETING_ROOM_TYPE.id });
  });

  it('offers a retry when the report fails to load, and Refresh reloads it', async () => {
    getUsage.mockReturnValueOnce(throwError(() => new Error('boom')));
    await render();
    expect(text(element.querySelector('[role="alert"]')!)).toContain('could not be loaded');

    button('Try again').click();
    await fixture.whenStable();
    expect(getUsage).toHaveBeenCalledTimes(2);
    expect(element.querySelectorAll('.tile')).toHaveLength(4);

    button('Refresh').click();
    await fixture.whenStable();
    expect(getUsage).toHaveBeenCalledTimes(3);
  });

  it('shows only the newest answer when the range changes during a load', async () => {
    const slow = new Subject<UsageReportData>();
    getUsage.mockReturnValueOnce(slow);
    await render({ from: '2026-09-01', to: '2026-09-30' });

    fixture.componentRef.setInput('to', '2026-09-15');
    await fixture.whenStable();
    slow.next(report({ totals: { ...report().totals, reservations: 999 } }));
    await fixture.whenStable();

    expect(text(element.querySelectorAll('.tile')[2])).toBe('14 reservations');
  });
});
