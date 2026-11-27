import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Announcement, NewAnnouncement } from '../../core/models/announcement.model';
import { ApiError } from '../../core/models/api-error.model';
import { AnnouncementService } from '../../core/services/announcement.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { AnnouncementsAdmin, endOfDay } from './announcements';

// Local wall-clock times, so what the page says reads the same in every time zone the tests run in.
const NOW = new Date(2026, 10, 27, 10, 0);
const local = (month: number, day: number, hours = 0) =>
  new Date(2026, month - 1, day, hours).toISOString();

function announcement(overrides: Partial<Announcement>): Announcement {
  return {
    id: 'ann',
    title: 'News',
    body: null,
    tone: 'Info',
    createdAt: local(11, 20, 9),
    endsAt: local(11, 28),
    authorId: 'admin-1',
    authorName: 'Zeynep Kaya',
    ...overrides,
  };
}

describe('AnnouncementsAdmin', () => {
  const breakfast = announcement({
    id: 'ann-1',
    title: 'Breakfast on Friday',
    tone: 'Good',
    endsAt: local(11, 28),
  });
  const lifts = announcement({
    id: 'ann-2',
    title: 'Lifts out of order',
    tone: 'Warning',
    endsAt: local(11, 27, 14),
    authorName: null,
  });
  const drill = announcement({ id: 'ann-3', title: 'Fire drill', endsAt: local(11, 24) });
  let service: {
    all: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<AnnouncementsAdmin>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    service = {
      all: vi.fn(() => of([breakfast, lifts, drill])),
      create: vi.fn((posted: NewAnnouncement) =>
        of(
          announcement({
            ...posted,
            id: 'ann-new',
            createdAt: NOW.toISOString(),
          }),
        ),
      ),
      remove: vi.fn(() => of(undefined)),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AnnouncementService, useValue: service },
        { provide: NotificationService, useValue: notifications },
        { provide: AuthService, useValue: { displayName: signal('Zeynep Kaya') } },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(AnnouncementsAdmin);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (node: Element) => node.textContent!.replace(/\s+/g, ' ').trim();
  // A row's title, what it says about it and its button are boxes of their own.
  const rows = () =>
    [...element.querySelectorAll('.item')].map((row) =>
      ['.name', '.meta', 'button'].map((part) => text(row.querySelector(part)!)).join(' '),
    );
  const preview = () => element.querySelector('.preview app-announcement-card')!;

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function post(): Promise<void> {
    element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  it('lists the ones posted: what kind, by whom, and until when or since when', async () => {
    await render();

    expect(rows()).toEqual([
      'Breakfast on Friday Good news · Zeynep Kaya · showing, last day Fri 27 Nov Take down',
      'Lifts out of order Heads-up · showing until Fri 27 Nov, 14:00 Take down',
      'Fire drill News · Zeynep Kaya · ended Mon 23 Nov Delete',
    ]);
    expect(element.querySelector('.item.ended')!.querySelector('.name')!.textContent).toBe(
      'Fire drill',
    );
  });

  it('shows it as it will look on the dashboard while it is written', async () => {
    await render();
    expect(text(preview().querySelector('h3')!)).toBe("Your announcement's title");

    await type('#announcement-title', 'Lifts out of order');
    await type('#announcement-body', 'Take the stairs\nuntil 14:00.');
    element.querySelector<HTMLInputElement>('.tone[data-tone="Warning"] input')!.click();
    await fixture.whenStable();

    expect(preview().getAttribute('data-tone')).toBe('Warning');
    expect(text(preview().querySelector('.kind')!)).toBe('Heads-up');
    expect(text(preview().querySelector('h3')!)).toBe('Lifts out of order');
    expect(preview().querySelector('.body')!.textContent).toBe('Take the stairs\nuntil 14:00.');
    expect(text(preview().querySelector('.meta')!)).toBe('Zeynep Kaya · Fri 27 Nov');
    expect(text(element.querySelector('.count')!)).toBe('28 / 1000');
  });

  it('posts one to show through its last day, and puts it first', async () => {
    await render();
    expect(text(element.querySelector('.chip[aria-pressed="true"]')!)).toBe('A week');

    await type('#announcement-title', '  Breakfast on Friday  ');
    await post();

    expect(service.create).toHaveBeenCalledWith({
      title: 'Breakfast on Friday',
      body: null,
      tone: 'Info',
      endsAt: local(12, 4),
      notify: false,
    });
    expect(notifications.success).toHaveBeenCalledWith("Posted: it's on everyone's dashboard now.");
    expect(rows()[0]).toBe(
      'Breakfast on Friday News · Zeynep Kaya · showing, last day Thu 3 Dec Take down',
    );
    expect(element.querySelector<HTMLInputElement>('#announcement-title')!.value).toBe('');
  });

  it('sends it to the phones too when asked, with a last day picked in a click', async () => {
    await render();

    await type('#announcement-title', 'Lifts out of order');
    [...element.querySelectorAll<HTMLButtonElement>('.chip')]
      .find((chip) => text(chip) === 'Tomorrow')!
      .click();
    element.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    await fixture.whenStable();
    await post();

    expect(service.create).toHaveBeenCalledWith(
      expect.objectContaining({ endsAt: local(11, 29), notify: true }),
    );
    expect(notifications.success).toHaveBeenCalledWith(
      'Posted, and sent to the phones with notifications on.',
    );
  });

  it('asks for a title and a day it can show until, and says what the API did not take', async () => {
    await render();

    await type('#announcement-title', '   ');
    await type('#announcement-last-day', '2026-11-26');
    await post();

    expect(service.create).not.toHaveBeenCalled();
    expect(text(element.querySelector('#announcement-title-error')!)).toBe('Give it a title.');
    expect(text(element.querySelector('#announcement-last-day-error')!)).toBe(
      'Pick a day from today to Mon 25 Jan.',
    );

    service.create.mockReturnValueOnce(
      throwError(
        () =>
          new ApiError(400, 'Bad Request', undefined, {
            EndsAt: ['An announcement shows for 60 days at most.'],
          }),
      ),
    );
    await type('#announcement-title', 'Breakfast');
    await type('#announcement-last-day', '2026-12-01');
    await post();

    expect(text(element.querySelector('[role="alert"]')!)).toBe(
      'An announcement shows for 60 days at most.',
    );
  });

  it('takes one down after asking, and leaves it when the question is declined', async () => {
    await render();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false);

    element.querySelector<HTMLButtonElement>('.item button')!.click();
    await fixture.whenStable();
    expect(service.remove).not.toHaveBeenCalled();

    confirm.mockReturnValueOnce(true);
    element.querySelector<HTMLButtonElement>('.item button')!.click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenLastCalledWith(
      "Take Breakfast on Friday down? It goes from everyone's dashboard at once.",
    );
    expect(service.remove).toHaveBeenCalledWith('ann-1');
    expect(rows()).toHaveLength(2);
    expect(notifications.success).toHaveBeenCalledWith('Breakfast on Friday was taken down.');
  });

  it('says when it could not load them, and tries again', async () => {
    service.all.mockReturnValueOnce(throwError(() => new ApiError(0, 'Offline')));
    await render();

    expect(text(element.querySelector('[role="alert"]')!)).toContain(
      'The announcements could not be loaded.',
    );
    element.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    await fixture.whenStable();

    expect(rows()).toHaveLength(3);
  });

  it('ends one as its last day does, but no later than the API takes', () => {
    expect(endOfDay('2026-12-03', NOW.getTime())).toBe(local(12, 4));
    expect(endOfDay('2027-03-01', NOW.getTime())).toBe(
      new Date(NOW.getTime() + 60 * 24 * 60 * 60_000).toISOString(),
    );
  });
});
