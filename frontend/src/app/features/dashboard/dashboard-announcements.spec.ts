import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Announcement } from '../../core/models/announcement.model';
import { AnnouncementService } from '../../core/services/announcement.service';
import { AuthService } from '../../core/services/auth.service';
import {
  ANNOUNCEMENTS_REFRESH_MS,
  DashboardAnnouncements,
  HIDDEN_ANNOUNCEMENTS_KEY,
} from './dashboard-announcements';

const ME = 'user-1';
const KEY = `${HIDDEN_ANNOUNCEMENTS_KEY}_${ME}`;

function announcement(id: string, title: string, overrides: Partial<Announcement> = {}) {
  return {
    id,
    title,
    body: null,
    tone: 'Info',
    createdAt: '2026-11-27T07:00:00Z',
    endsAt: '2026-12-04T21:00:00Z',
    authorId: 'admin-1',
    authorName: 'Zeynep Kaya',
    ...overrides,
  } satisfies Announcement;
}

describe('DashboardAnnouncements', () => {
  const breakfast = announcement('ann-1', 'Breakfast on Friday', { tone: 'Good' });
  const lifts = announcement('ann-2', 'Lifts out of order', { tone: 'Warning' });
  let current: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<DashboardAnnouncements>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    localStorage.clear();
    current = vi.fn(() => of([lifts, breakfast]));
    TestBed.configureTestingModule({
      providers: [
        { provide: AnnouncementService, useValue: { current } },
        { provide: AuthService, useValue: { currentUser: signal({ id: ME }) } },
      ],
    });
  });

  afterEach(() => {
    fixture?.destroy();
    vi.useRealTimers();
    localStorage.clear();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(DashboardAnnouncements);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const titles = () => [...element.querySelectorAll('h3')].map((h) => h.textContent!.trim());

  it('shows the ones showing now, newest first, under a heading for screen readers', async () => {
    await render();

    expect(titles()).toEqual(['Lifts out of order', 'Breakfast on Friday']);
    expect(element.querySelector('h2')!.textContent!.trim()).toBe('Announcements');
    expect(element.querySelector('app-announcement-card')!.getAttribute('data-tone')).toBe(
      'Warning',
    );
  });

  it('shows nothing at all when there are none', async () => {
    current.mockReturnValue(of([]));

    await render();

    expect(element.querySelector('section')).toBeNull();
  });

  it('hides one for this person on this device, and keeps it hidden next time', async () => {
    await render();

    element.querySelector<HTMLButtonElement>('.dismiss')!.click();
    await fixture.whenStable();

    expect(titles()).toEqual(['Breakfast on Friday']);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual(['ann-2']);

    fixture.destroy();
    await render();

    expect(titles()).toEqual(['Breakfast on Friday']);
  });

  it('forgets a hidden one once it has ended', async () => {
    localStorage.setItem(KEY, JSON.stringify(['ann-2', 'ann-gone']));

    await render();

    expect(titles()).toEqual(['Breakfast on Friday']);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual(['ann-2']);
  });

  it('asks again every minute, and keeps what it shows when asking fails', async () => {
    await render();
    const news = announcement('ann-3', 'Welcome, Ayşe');

    current.mockReturnValue(of([news, lifts, breakfast]));
    vi.advanceTimersByTime(ANNOUNCEMENTS_REFRESH_MS);
    await fixture.whenStable();
    expect(titles()).toEqual(['Welcome, Ayşe', 'Lifts out of order', 'Breakfast on Friday']);

    current.mockReturnValue(throwError(() => new Error('offline')));
    vi.advanceTimersByTime(ANNOUNCEMENTS_REFRESH_MS);
    await fixture.whenStable();
    expect(titles()).toHaveLength(3);
  });

  it('reads what was kept, whatever is in it', async () => {
    localStorage.setItem(KEY, '{not json');

    await render();

    expect(titles()).toHaveLength(2);
  });
});
