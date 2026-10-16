import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { IssueReport } from '../../core/models/issue.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { IssueService } from '../../core/services/issue.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { fakeIssue } from '../../../testing/issue-fixtures';
import { fakeObjectUrls } from '../../../testing/object-urls';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { Issues } from './issues';

// Local times, so what the screen prints reads the same in every time zone the tests run in.
const at = (local: string) => new Date(`2026-10-07T${local}`);
const iso = (local: string) => at(local).toISOString();

describe('Issues', () => {
  let getAll: ReturnType<typeof vi.fn>;
  let updateStatus: ReturnType<typeof vi.fn>;
  let getPhoto: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let remove: ReturnType<typeof vi.fn>;
  let getCalendar: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  let reported: Subject<IssueReport>;
  let updated: Subject<IssueReport>;
  let fixture: ComponentFixture<Issues>;
  let element: HTMLElement;

  const dirty = fakeIssue({
    id: 'dirty',
    resourceId: 'res-2',
    resourceName: 'Board Room',
    category: 'Cleaning',
    description: 'Coffee on the table',
    createdAt: iso('11:00'),
  });
  const noAir = fakeIssue({
    id: 'no-air',
    category: 'AirConditioning',
    description: 'Too hot to sit in',
    isBlocking: true,
    reportedByName: 'Ayşe Yılmaz',
    createdAt: iso('10:00'),
  });
  const fixedTv = fakeIssue({
    id: 'fixed',
    status: 'Resolved',
    createdAt: iso('08:00'),
    resolvedAt: iso('09:15'),
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(at('12:00'));
    getAll = vi.fn(() => of([fixedTv, dirty, noAir]));
    updateStatus = vi.fn((id: string, status: string) =>
      of({ ...[fixedTv, dirty, noAir].find((i) => i.id === id)!, status }),
    );
    getPhoto = vi.fn(() => of(new Blob(['png'], { type: 'image/png' })));
    getBlocks = vi.fn(() => of([]));
    create = vi.fn(
      (request: {
        resourceId: string;
        startDateTime: string;
        endDateTime: string;
        reason: string;
      }) =>
        of({ id: 'blk-1', resourceName: 'Meeting Room A', createdByUserId: 'u-admin', ...request }),
    );
    remove = vi.fn(() => of(undefined));
    getCalendar = vi.fn(() => of([]));
    success = vi.fn();
    error = vi.fn();
    confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);
    reported = new Subject();
    updated = new Subject();

    TestBed.configureTestingModule({
      providers: [
        { provide: IssueService, useValue: { getAll, updateStatus, getPhoto } },
        { provide: ResourceBlockService, useValue: { getBlocks, create, delete: remove } },
        { provide: ReservationService, useValue: { getCalendar } },
        {
          provide: ReservationHubService,
          useValue: {
            issueReported$: reported,
            issueUpdated$: updated,
            reconnected$: new Subject(),
          },
        },
        { provide: NotificationService, useValue: { success, error } },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Issues);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  const cards = () => [...element.querySelectorAll('.issue')];
  const titles = () => cards().map((card) => text(card.querySelector('.what')!));
  const card = (title: string) => cards().find((c) => text(c.querySelector('.what')!) === title)!;
  const button = (scope: Element, label: string) =>
    [...scope.querySelectorAll<HTMLButtonElement>('button')].find((b) => text(b) === label);

  async function select(id: string, value: string): Promise<void> {
    const box = element.querySelector<HTMLSelectElement>(`#${id}`)!;
    box.value = value;
    box.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  async function click(target: HTMLElement): Promise<void> {
    target.click();
    await fixture.whenStable();
  }

  /** The closing form counts the bookings whenever its end moves. */
  async function counted(): Promise<void> {
    await fixture.whenStable();
  }

  it('lists what is still to fix, the unusable first, with who reported it and when', async () => {
    await render();

    expect(titles()).toEqual(['Air conditioning · Meeting Room A', 'Cleaning · Board Room']);
    const first = cards()[0];
    expect(text(first.querySelector('.description')!)).toBe('Too hot to sit in');
    expect(text(first.querySelector('.meta')!)).toBe('Reported by Ayşe Yılmaz · 10:00');
    expect(first.querySelector('.blocking-badge')).not.toBeNull();
    expect(text(element.querySelector('.summary')!)).toBe('2 open, 0 in progress');
  });

  it('says a report came from the door without an account, naming nobody', async () => {
    getAll.mockReturnValue(of([{ ...dirty, reportedByUserId: null, reportedByName: null }, noAir]));
    await render();

    expect(text(cards()[1].querySelector('.meta')!)).toBe(
      'Reported at the door, without an account · 11:00',
    );
  });

  it('shows the resolved ones, or one resource, when asked', async () => {
    await render();

    await select('issue-status', 'Resolved');
    expect(titles()).toEqual(['Screen or TV · Meeting Room A']);
    expect(text(cards()[0].querySelector('.meta')!)).toBe(
      'Reported by Zeynep Kaya · 08:00 · resolved 09:15',
    );
    expect(button(cards()[0], 'Mark resolved')).toBeUndefined();

    await select('issue-status', 'all');
    await select('issue-resource', 'res-2');
    expect(titles()).toEqual(['Cleaning · Board Room']);
  });

  it('takes a report up, and resolves it once the user says so', async () => {
    await render();
    const room = 'Air conditioning · Meeting Room A';

    await click(button(card(room), 'Start on it')!);
    expect(updateStatus).toHaveBeenCalledWith('no-air', 'InProgress');
    expect(text(card(room).querySelector('.status')!)).toBe('In progress');

    confirm.mockReturnValueOnce(false);
    await click(button(card(room), 'Mark resolved')!);
    expect(updateStatus).toHaveBeenCalledTimes(1);

    await click(button(card(room), 'Mark resolved')!);
    expect(confirm).toHaveBeenLastCalledWith(
      "Mark the problem with Meeting Room A resolved? Whoever reported it is told, and it can't be opened again.",
    );
    expect(updateStatus).toHaveBeenLastCalledWith('no-air', 'Resolved');
    expect(success).toHaveBeenCalledWith('The problem with Meeting Room A is resolved.');
    expect(titles()).toEqual(['Cleaning · Board Room']);
  });

  it('says why when a report was resolved meanwhile, and loads the list again', async () => {
    updateStatus.mockReturnValue(
      throwError(
        () =>
          new ApiError(409, 'Conflict', 'This report is already resolved.', {}, 'issue-resolved'),
      ),
    );
    await render();

    await click(button(cards()[0], 'Start on it')!);

    expect(error).toHaveBeenCalledWith('This report is already resolved.');
    expect(getAll).toHaveBeenCalledTimes(2);
  });

  it('follows reports made and moved elsewhere, as they happen', async () => {
    await render();

    reported.next(
      fakeIssue({
        id: 'new',
        category: 'Projector',
        resourceName: 'Studio',
        createdAt: iso('11:59'),
      }),
    );
    updated.next({ ...dirty, status: 'Resolved', resolvedAt: iso('11:58') });
    await fixture.whenStable();

    expect(titles()).toEqual(['Air conditioning · Meeting Room A', 'Projector · Studio']);
  });

  it('says when there is nothing to fix, and offers a retry when loading fails', async () => {
    getAll.mockReturnValue(of([fixedTv]));
    await render();
    expect(text(element.querySelector('.empty')!)).toContain('Nothing to fix.');

    getAll.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    await render();
    expect(element.textContent).toContain('The reported problems could not be loaded.');
    getAll.mockReturnValue(of([noAir]));
    await click(button(element, 'Try again')!);
    expect(titles()).toEqual(['Air conditioning · Meeting Room A']);
  });

  it('shows the photo of a report that has one, opening the whole picture', async () => {
    const urls = fakeObjectUrls();
    getAll.mockReturnValue(of([{ ...noAir, hasPhoto: true }]));
    await render();

    expect(getPhoto).toHaveBeenCalledWith('no-air');
    const link = element.querySelector<HTMLAnchorElement>('app-issue-photo a')!;
    expect(link.getAttribute('href')).toBe('blob:test-1');
    expect(link.target).toBe('_blank');
    expect(link.getAttribute('aria-label')).toBe(
      'Photo of the problem with Meeting Room A, opens in a new tab',
    );
    fixture.destroy();
    expect(urls.revoked).toEqual(['blob:test-1']);
    urls.restore();
  });

  describe('taking a resource out of use (ADR-0038)', () => {
    const room = 'Air conditioning · Meeting Room A';

    async function openForm(): Promise<HTMLElement> {
      await render();
      await click(button(card(room), 'Take it out of use')!);
      await counted();
      return card(room).querySelector<HTMLElement>('app-take-out-of-use')!;
    }

    it('closes it until the end of today, saying first how many bookings go', async () => {
      getCalendar.mockReturnValue(
        of([
          fakeReservation({ id: 'a', startDateTime: iso('11:30'), endDateTime: iso('12:30') }),
          fakeReservation({ id: 'b', startDateTime: iso('15:00'), endDateTime: iso('16:00') }),
          fakeReservation({
            id: 'c',
            status: 'Cancelled',
            startDateTime: iso('17:00'),
            endDateTime: iso('18:00'),
          }),
          fakeReservation({ id: 'd', startDateTime: iso('10:00'), endDateTime: iso('11:00') }),
        ]),
      );
      const form = await openForm();

      expect(getCalendar).toHaveBeenCalledWith({
        from: at('12:00').toISOString(),
        to: new Date('2026-10-08T00:00').toISOString(),
        resourceId: 'res-1',
      });
      expect(text(form.querySelector('.impact')!)).toBe(
        'Closed until Thu 8 Oct, 00:00. 2 bookings in that time are cancelled, and everyone on them is told why.',
      );
      expect(form.querySelector<HTMLInputElement>('#close-reason')!.value).toBe(
        'Air conditioning: Too hot to sit in',
      );

      form.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      expect(create).toHaveBeenCalledWith({
        resourceId: 'res-1',
        startDateTime: expect.any(String),
        endDateTime: new Date('2026-10-08T00:00').toISOString(),
        reason: 'Air conditioning: Too hot to sit in',
        cancelReservations: true,
      });
      expect(success).toHaveBeenCalledWith('Meeting Room A is closed until Thu 8 Oct, 00:00.');
      expect(text(card(room).querySelector('.closed')!)).toBe(
        'Closed until Thu 8 Oct, 00:00: Air conditioning: Too hot to sit in',
      );
      expect(button(card(room), 'Open it again')).toBeDefined();
    });

    it('cancels nothing when nobody has booked it, for as long as chosen', async () => {
      const form = await openForm();
      expect(text(form.querySelector('.impact')!)).toContain('Nobody has booked it in that time.');

      form.querySelector<HTMLInputElement>('input[value="week"]')!.click();
      await counted();
      expect(text(form.querySelector('.impact')!)).toContain('Closed until Thu 15 Oct, 00:00.');

      form.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          endDateTime: new Date('2026-10-15T00:00').toISOString(),
          cancelReservations: false,
        }),
      );
    });

    it('says why it could not be closed, and counts the bookings again', async () => {
      const reason = '1 confirmed reservation(s) on Meeting Room A overlap this window.';
      create.mockReturnValue(
        throwError(() => new ApiError(409, 'Conflict', reason, {}, 'block-overlaps-reservations')),
      );
      const form = await openForm();
      getCalendar.mockClear();

      form.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
      await counted();

      expect(text(form.querySelector('[role="alert"]')!)).toBe(reason);
      expect(getCalendar).toHaveBeenCalled();
      expect(card(room).querySelector('.closed')).toBeNull();
    });

    it('offers to open it again once its problem is resolved, without doing it unasked', async () => {
      getBlocks.mockReturnValue(
        of([
          {
            id: 'blk-7',
            resourceId: 'res-1',
            resourceName: 'Meeting Room A',
            startDateTime: iso('11:00'),
            endDateTime: new Date('2026-10-08T00:00').toISOString(),
            reason: 'Air conditioning: Too hot to sit in',
            createdByUserId: 'u-admin',
          },
        ]),
      );
      await render();

      // Resolved, but kept closed: another problem there may need it.
      confirm.mockReturnValueOnce(true).mockReturnValueOnce(false);
      await click(button(card(room), 'Mark resolved')!);
      expect(confirm).toHaveBeenLastCalledWith(
        'Meeting Room A is still closed until Thu 8 Oct, 00:00. Open it for booking again now?',
      );
      expect(remove).not.toHaveBeenCalled();

      // Another report on it, resolved and opened again.
      const tv: IssueReport = { ...noAir, id: 'tv', category: 'Display', status: 'Open' };
      updateStatus.mockReturnValue(of({ ...tv, status: 'Resolved' }));
      updated.next(tv);
      await fixture.whenStable();
      confirm.mockReturnValue(true);
      await click(button(card('Screen or TV · Meeting Room A'), 'Mark resolved')!);
      expect(remove).toHaveBeenCalledWith('blk-7');
    });

    it('opens a closed resource again, once the user says so', async () => {
      const block: ResourceBlock = {
        id: 'blk-9',
        resourceId: 'res-1',
        resourceName: 'Meeting Room A',
        startDateTime: iso('11:00'),
        endDateTime: new Date('2026-10-08T00:00').toISOString(),
        reason: 'Air conditioning: Too hot to sit in',
        createdByUserId: 'u-admin',
      };
      getBlocks.mockReturnValue(of([block]));
      await render();

      expect(button(card(room), 'Take it out of use')).toBeUndefined();
      await click(button(card(room), 'Open it again')!);

      expect(remove).toHaveBeenCalledWith('blk-9');
      expect(success).toHaveBeenCalledWith('Meeting Room A can be booked again.');
      expect(card(room).querySelector('.closed')).toBeNull();
      expect(button(card(room), 'Take it out of use')).toBeDefined();
    });
  });
});
