import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { NotificationService } from '../../core/services/notification.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { ResourceBlocks } from './resource-blocks';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('ResourceBlocks', () => {
  const NOW = new Date('2026-10-05T10:07'); // a Monday, local time

  const room = fakeResource(); // Meeting Room A, Active, 2nd Floor
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    location: '3rd Floor',
  });
  const camera = fakeResource({ id: 'res-3', name: 'Camera C-1', status: 'Maintenance' });

  const block = (id: string, start: string, end: string, extra: Partial<ResourceBlock> = {}) =>
    ({
      id,
      resourceId: room.id,
      resourceName: room.name,
      startDateTime: utc(start),
      endDateTime: utc(end),
      reason: 'Projector replacement',
      createdByUserId: 'admin-1',
      ...extra,
    }) satisfies ResourceBlock;
  const ended = block('blk-1', '2026-10-05T06:00', '2026-10-05T08:00', { reason: 'Cleaning' });
  const running = block('blk-2', '2026-10-05T09:00', '2026-10-05T12:00');
  const painting = block('blk-3', '2026-10-12T09:00', '2026-10-14T18:00', {
    resourceId: desk.id,
    resourceName: desk.name,
    reason: 'Painting',
  });

  let getResources: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let remove: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ResourceBlocks>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    getResources = vi.fn(() => of([room, desk, camera]));
    getBlocks = vi.fn(() => of([painting, running, ended]));
    create = vi.fn((body) =>
      of({ id: 'blk-new', resourceName: 'Meeting Room A', createdByUserId: 'admin-1', ...body }),
    );
    remove = vi.fn(() => of(undefined));
    success = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);

    await TestBed.configureTestingModule({
      imports: [ResourceBlocks],
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResources } },
        { provide: ResourceBlockService, useValue: { getBlocks, create, delete: remove } },
        { provide: NotificationService, useValue: { success, error: vi.fn() } },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    confirmSpy.mockRestore();
  });

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(ResourceBlocks);
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

  function field<T extends HTMLElement = HTMLInputElement>(selector: string): T {
    return element.querySelector<T>(selector)!;
  }

  async function set(selector: string, value: string): Promise<void> {
    const el = field<HTMLInputElement | HTMLSelectElement>(selector);
    el.value = value;
    el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input'));
    el.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  async function fillValidBlock(): Promise<void> {
    await set('#block-resource', room.id);
    await set('#block-start', '2026-10-06T09:00');
    await set('#block-end', '2026-10-06T13:00');
    await set('#block-reason', '  Projector replacement ');
  }

  describe('list', () => {
    it('asks for four weeks starting today, in UTC', async () => {
      await render();

      expect(getBlocks).toHaveBeenCalledWith({
        from: utc('2026-10-05T00:00'),
        to: utc('2026-11-02T00:00'),
        resourceId: null,
      });
    });

    it('uses the range and resource from the query string, last day included', async () => {
      await render({ from: '2026-10-10', to: '2026-10-12', resource: desk.id });

      expect(getBlocks).toHaveBeenCalledWith({
        from: utc('2026-10-10T00:00'),
        to: utc('2026-10-13T00:00'),
        resourceId: desk.id,
      });
      expect(field<HTMLSelectElement>('#resource').value).toBe(desk.id);
    });

    it('lists the blocks earliest first; ended ones can no longer be removed', async () => {
      await render();

      expect(rows()).toEqual([
        'Mon 5 Oct, 06:00-08:00 2 h Meeting Room A Meeting Room · 2nd Floor Cleaning',
        'Mon 5 Oct, 09:00-12:00 3 h In progress Meeting Room A Meeting Room · 2nd Floor Projector replacement Remove',
        'Mon 12 Oct, 09:00 to Wed 14 Oct, 18:00 2 days 9 h Desk 12 Desk · 3rd Floor Painting Remove',
      ]);
      expect(text(field('.summary'))).toBe('3 blocks from Mon 5 Oct to Sun 1 Nov.');
    });

    it('says when the range has no blocks', async () => {
      getBlocks.mockReturnValue(of([]));
      await render();

      expect(text(field('.summary'))).toBe('0 blocks from Mon 5 Oct to Sun 1 Nov.');
      expect(element.textContent).toContain('No blocks in this range.');
    });

    it('refuses a range the API would reject, without asking it', async () => {
      await render({ from: '2026-10-10', to: '2026-10-09' });
      expect(element.textContent).toContain('The last day is before the first day.');

      fixture.componentRef.setInput('to', '2026-11-30');
      await fixture.whenStable();
      expect(element.textContent).toContain('Show at most 42 days at a time.');
      expect(getBlocks).not.toHaveBeenCalled();
    });

    it('accepts six weeks across the autumn clock change, like the API', async () => {
      await render({ from: '2026-10-01', to: '2026-11-11' });

      expect(element.textContent).not.toContain('Show at most');
      expect(getBlocks).toHaveBeenCalledWith(
        expect.objectContaining({ from: utc('2026-10-01T00:00'), to: utc('2026-11-12T00:00') }),
      );
    });

    it('offers to try again when the list cannot be loaded', async () => {
      getBlocks.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
      await render();
      expect(element.textContent).toContain('Blocks could not be loaded.');

      element.querySelector<HTMLButtonElement>('.load-error button')!.click();
      await fixture.whenStable();

      expect(getBlocks).toHaveBeenCalledTimes(2);
      expect(rows().length).toBe(3);
    });

    it('puts the chosen range and resource in the query string', async () => {
      await render();

      const from = field('#from');
      from.value = '2026-10-12';
      from.dispatchEvent(new Event('change'));
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({
          queryParams: { from: '2026-10-12' },
          queryParamsHandling: 'merge',
        }),
      );
      await set('#resource', desk.id);
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { resource: desk.id } }),
      );
      await set('#resource', '');
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { resource: null } }),
      );
    });

    it('removes a block after confirming, and says so', async () => {
      await render();

      element.querySelectorAll<HTMLButtonElement>('.remove')[0].click();
      await fixture.whenStable();

      expect(confirmSpy).toHaveBeenCalledWith(
        'Remove the block on Meeting Room A for Mon 5 Oct, 09:00-12:00? It can then be booked in that window.',
      );
      expect(remove).toHaveBeenCalledWith('blk-2');
      expect(success).toHaveBeenCalledWith(
        'The block on Meeting Room A for Mon 5 Oct, 09:00-12:00 was removed.',
      );
      expect(rows().length).toBe(2);
    });

    it('keeps the block when the admin changes their mind', async () => {
      confirmSpy.mockReturnValue(false);
      await render();

      element.querySelectorAll<HTMLButtonElement>('.remove')[0].click();
      await fixture.whenStable();

      expect(remove).not.toHaveBeenCalled();
      expect(rows().length).toBe(3);
    });

    it('reloads the list when removing fails, e.g. someone removed it already', async () => {
      remove.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
      getBlocks
        .mockReturnValueOnce(of([painting, running, ended]))
        .mockReturnValueOnce(of([painting, ended]));
      await render();

      element.querySelectorAll<HTMLButtonElement>('.remove')[0].click();
      await fixture.whenStable();

      expect(getBlocks).toHaveBeenCalledTimes(2);
      expect(rows().length).toBe(2);
      expect(success).not.toHaveBeenCalled();
    });
  });

  describe('form', () => {
    it('starts at the next half hour for an hour, with every resource by type', async () => {
      await render();

      expect(field('#block-start').value).toBe('2026-10-05T10:30');
      expect(field('#block-end').value).toBe('2026-10-05T11:30');
      const groups = Array.from(field('#block-resource').querySelectorAll('optgroup')).map(
        (g) =>
          `${g.label}: ${Array.from(g.querySelectorAll('option'))
            .map((o) => o.textContent!.trim())
            .join(', ')}`,
      );
      expect(groups).toEqual([
        'Desk: Desk 12',
        'Meeting Room: Camera C-1 (Maintenance), Meeting Room A',
      ]);
    });

    it('shows the missing fields and does not call the API', async () => {
      await render();
      await set('#block-reason', '   ');
      await submit();

      expect(create).not.toHaveBeenCalled();
      expect(element.textContent).toContain('Choose a resource.');
      expect(element.textContent).toContain('Say why the resource is blocked.');
    });

    it('mirrors the API time rules: 5-minute grace, end after start, at most 366 days', async () => {
      await render();
      await set('#block-resource', room.id);
      await set('#block-reason', 'Maintenance');

      await set('#block-start', '2026-10-05T10:05'); // 2 minutes ago, within the grace
      await set('#block-end', '2026-10-05T11:00');
      expect(element.textContent).not.toContain('The start time is in the past.');

      await set('#block-start', '2026-10-05T10:00'); // 7 minutes ago
      await set('#block-end', '2026-10-05T11:00');
      expect(element.textContent).toContain('The start time is in the past.');

      await set('#block-start', '2026-10-05T10:15');
      await set('#block-end', '2026-10-05T10:15');
      expect(element.textContent).toContain('The end must be after the start.');

      await set('#block-end', '2027-10-06T10:30');
      expect(element.textContent).toContain('A block can last at most 366 days.');

      await submit();
      expect(create).not.toHaveBeenCalled();
    });

    it('blocks the resource with UTC times and a trimmed reason, then starts over', async () => {
      await render();
      await fillValidBlock();
      await submit();

      expect(create).toHaveBeenCalledWith({
        resourceId: room.id,
        startDateTime: utc('2026-10-06T09:00'),
        endDateTime: utc('2026-10-06T13:00'),
        reason: 'Projector replacement',
      });
      expect(success).toHaveBeenCalledWith('Meeting Room A is blocked for Tue 6 Oct, 09:00-13:00.');
      expect(rows()[2]).toContain('Tue 6 Oct, 09:00-13:00');
      expect(field<HTMLSelectElement>('#block-resource').value).toBe('');
      expect(field('#block-reason').value).toBe('');
      expect(element.querySelector('.field-error')).toBeNull();
    });

    it('leaves a new block out of the list when it is outside the range shown', async () => {
      await render({ from: '2026-10-10', to: '2026-10-20' });
      getBlocks.mockClear();
      await fillValidBlock(); // on the 6th
      await submit();

      expect(create).toHaveBeenCalled();
      expect(rows().some((row) => row.includes('Tue 6 Oct'))).toBe(false);
      expect(getBlocks).not.toHaveBeenCalled();
    });

    it('shows an overlapping block as the API words it', async () => {
      const message = 'Meeting Room A is already blocked for part of that time.';
      create.mockReturnValue(throwError(() => new ApiError(409, 'Conflict', message)));
      await render();
      await fillValidBlock();
      await submit();

      const alert = field('.conflict');
      expect(text(alert)).toBe(message);
      expect(alert.querySelector('a')).toBeNull();
    });

    it('links to the reservations that stand in the way', async () => {
      const message =
        '2 confirmed reservation(s) on Meeting Room A overlap this window. Cancel or move them first.';
      create.mockReturnValue(throwError(() => new ApiError(409, 'Conflict', message)));
      await render();
      await fillValidBlock();
      await submit();

      const alert = field('.conflict');
      expect(text(alert)).toContain(message);
      const href = alert.querySelector('a')!.getAttribute('href')!;
      expect(href).toBe('/admin?from=2026-10-06&to=2026-10-06&type=type-room');
    });

    it('opens a long window on its first six weeks in the admin overview', async () => {
      create.mockReturnValue(
        throwError(
          () =>
            new ApiError(
              409,
              'Conflict',
              '1 confirmed reservation(s) on Desk 12 overlap this window.',
            ),
        ),
      );
      await render();
      await set('#block-resource', desk.id);
      await set('#block-start', '2026-10-06T00:00');
      await set('#block-end', '2026-12-01T00:00');
      await set('#block-reason', 'Renovation');
      await submit();

      expect(field('.conflict a').getAttribute('href')).toBe(
        '/admin?from=2026-10-06&to=2026-11-16&type=type-desk',
      );
    });

    it('puts the API validation messages next to their fields', async () => {
      create.mockReturnValue(
        throwError(
          () =>
            new ApiError(400, 'Validation failed', null, {
              Reason: ['Reason must be 200 characters or fewer.'],
              Other: ['Something else.'],
            }),
        ),
      );
      await render();
      await fillValidBlock();
      await submit();

      expect(element.textContent).toContain('Reason must be 200 characters or fewer.');
      expect(text(field('.alert-error'))).toBe('Something else.');
    });

    it('keeps a note on the form when saving fails otherwise', async () => {
      create.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render();
      await fillValidBlock();
      await submit();

      expect(element.textContent).toContain('The block could not be saved.');
      expect(field('#block-reason').value).toBe('  Projector replacement ');
    });
  });
});
