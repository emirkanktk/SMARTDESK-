import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { Availability } from './availability';

// Local wall-clock time -> the UTC ISO string the API expects, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('Availability', () => {
  const NOW = new Date('2026-10-05T10:07'); // local time

  let getAvailable: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<Availability>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getAvailable = vi.fn(() => of([fakeResource()]));

    await TestBed.configureTestingModule({
      imports: [Availability],
      providers: [
        provideRouter([]),
        {
          provide: ResourceService,
          useValue: { getResourceTypes: () => of([DESK_TYPE, MEETING_ROOM_TYPE]), getAvailable },
        },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => vi.useRealTimers());

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(Availability);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function input(selector: string): HTMLInputElement {
    return element.querySelector<HTMLInputElement>(selector)!;
  }

  async function set(selector: string, value: string): Promise<void> {
    const el = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input'));
    el.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('starts with the next half hour for one hour and does not search yet', async () => {
    await render();

    expect(input('#start').value).toBe('2026-10-05T10:30');
    expect(input('#end').value).toBe('2026-10-05T11:30');
    expect(element.textContent).toContain('1 h');
    expect(getAvailable).not.toHaveBeenCalled();
  });

  it('searches with UTC times and filters, and keeps the search in the URL', async () => {
    await render();
    await set('#type', MEETING_ROOM_TYPE.id);
    await set('#capacity', '4');
    await submit();

    const query = {
      startDateTime: utc('2026-10-05T10:30'),
      endDateTime: utc('2026-10-05T11:30'),
      resourceTypeId: MEETING_ROOM_TYPE.id,
      minCapacity: 4,
    };
    expect(getAvailable).toHaveBeenCalledWith(query);
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({
        queryParams: {
          start: query.startDateTime,
          end: query.endDateTime,
          type: MEETING_ROOM_TYPE.id,
          capacity: 4,
        },
      }),
    );
  });

  it('lists free resources with a Book link carrying the resource and the searched times', async () => {
    await render();
    await submit();

    expect(element.querySelector('.summary')?.textContent).toContain('1');
    expect(element.querySelector('h2')?.textContent).toContain('Meeting Room A');
    const href = element.querySelector<HTMLAnchorElement>('a.book')!.getAttribute('href')!;
    const params = new URLSearchParams(href.split('?')[1]);
    expect(href.startsWith('/reservations/new?')).toBe(true);
    expect(params.get('resourceId')).toBe('res-1');
    expect(params.get('start')).toBe(utc('2026-10-05T10:30'));
    expect(params.get('end')).toBe(utc('2026-10-05T11:30'));
  });

  it('says so when nothing is free', async () => {
    getAvailable.mockReturnValue(of([]));
    await render();
    await submit();

    expect(element.textContent).toContain('Nothing is free');
    expect(element.querySelector('a.book')).toBeNull();
  });

  it('rejects an end before the start without calling the API', async () => {
    await render();
    await set('#end', '2026-10-05T10:00');
    await submit();

    expect(element.textContent).toContain('The end must be after the start.');
    expect(getAvailable).not.toHaveBeenCalled();
  });

  it('rejects a start in the past', async () => {
    await render();
    await set('#start', '2026-10-05T09:00');
    await submit();

    expect(element.textContent).toContain('The start time is in the past.');
    expect(getAvailable).not.toHaveBeenCalled();
  });

  it('rejects a range longer than 31 days', async () => {
    await render();
    await set('#end', '2026-11-10T10:00');
    await submit();

    expect(element.textContent).toContain('at most 31 days');
    expect(getAvailable).not.toHaveBeenCalled();
  });

  it('moves the end with the start, keeping the duration', async () => {
    await render();
    await set('#end', '2026-10-05T12:30'); // 2 h
    await set('#start', '2026-10-06T09:00');

    expect(input('#end').value).toBe('2026-10-06T11:00');
  });

  it('restores and runs a search from the URL', async () => {
    await render({
      start: utc('2026-10-07T14:00'),
      end: utc('2026-10-07T15:30'),
      type: DESK_TYPE.id,
      capacity: '2',
    });

    expect(input('#start').value).toBe('2026-10-07T14:00');
    expect(input('#end').value).toBe('2026-10-07T15:30');
    expect(getAvailable).toHaveBeenCalledWith({
      startDateTime: utc('2026-10-07T14:00'),
      endDateTime: utc('2026-10-07T15:30'),
      resourceTypeId: DESK_TYPE.id,
      minCapacity: 2,
    });
  });

  it('does not run a search from an old link whose start has passed', async () => {
    await render({ start: utc('2026-10-01T14:00'), end: utc('2026-10-01T15:00') });

    expect(getAvailable).not.toHaveBeenCalled();
    expect(element.textContent).toContain('The start time is in the past.');
  });

  it('shows the API validation message next to the field', async () => {
    getAvailable.mockReturnValue(
      throwError(() => new ApiError(400, 'Invalid', null, { MinCapacity: ['Must be positive.'] })),
    );
    await render();
    await submit();

    expect(element.textContent).toContain('Must be positive.');
  });
});
