import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { CalendarSubscription } from '../../../core/models/calendar.model';
import { CalendarService } from '../../../core/services/calendar.service';
import { NotificationService } from '../../../core/services/notification.service';
import { CalendarLink, onTheInternet } from './calendar-link';

describe('CalendarLink', () => {
  const token = 'a'.repeat(64);
  const made: CalendarSubscription = {
    url: `https://smartdesk.example.com/api/calendar/feed.ics?token=${token}`,
    createdAt: '2026-10-15T08:00:00Z',
    lastFetchedAt: null,
  };

  let getSubscription: ReturnType<typeof vi.fn>;
  let createSubscription: ReturnType<typeof vi.fn>;
  let deleteSubscription: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<CalendarLink>;
  let element: HTMLElement;

  beforeEach(() => {
    getSubscription = vi.fn(() => of(null));
    createSubscription = vi.fn(() => of(made));
    deleteSubscription = vi.fn(() => of(undefined));
    success = vi.fn();
    error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: CalendarService,
          useValue: { getSubscription, createSubscription, deleteSubscription },
        },
        { provide: NotificationService, useValue: { success, error } },
      ],
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(CalendarLink);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function button(text: string): HTMLButtonElement | undefined {
    return [...element.querySelectorAll('button')].find((b) => b.textContent!.trim() === text);
  }

  function link(text: string): HTMLAnchorElement | undefined {
    return [...element.querySelectorAll('a')].find((a) => a.textContent!.trim() === text);
  }

  it('makes a link and shows it this once, to copy, open in a calendar app or add to Google', async () => {
    await render();
    expect(element.textContent).toContain('Subscribe to your link once');

    button('Make my calendar link')!.click();
    await fixture.whenStable();

    expect(createSubscription).toHaveBeenCalledOnce();
    const input = element.querySelector<HTMLInputElement>('input.link')!;
    expect(input.value).toBe(made.url);
    expect(input.readOnly).toBe(true);
    expect(element.querySelector('.new-link')!.textContent).toContain("It's shown only now");
    expect(link('Open in my calendar app')!.getAttribute('href')).toBe(
      `webcal://smartdesk.example.com/api/calendar/feed.ics?token=${token}`,
    );
    expect(link('Add to Google Calendar')!.href).toBe(
      `https://calendar.google.com/calendar/r?cid=${encodeURIComponent(
        `webcal://smartdesk.example.com/api/calendar/feed.ics?token=${token}`,
      )}`,
    );
    expect(element.querySelector('.off-internet')).toBeNull();
    expect(element.querySelector('.state')!.textContent).toContain(
      'No calendar has fetched it yet.',
    );
    expect(button('New link')).toBeDefined();
    expect(button('Make my calendar link')).toBeUndefined();

    const writeText = vi.fn(() => Promise.resolve());
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    button('Copy link')!.click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith(made.url);
    expect(success).toHaveBeenCalledWith('Link copied.');
  });

  it('says when the link is not on the internet, which Google and Outlook on the web need', async () => {
    createSubscription.mockReturnValue(
      of({ ...made, url: `http://localhost:4200/api/calendar/feed.ics?token=${token}` }),
    );
    await render();

    button('Make my calendar link')!.click();
    await fixture.whenStable();

    expect(element.querySelector('.off-internet')!.textContent).toContain("isn't on the internet");
  });

  it('shows an existing link by its dates only, and replaces it only when that is confirmed', async () => {
    getSubscription.mockReturnValue(
      of({ url: null, createdAt: '2026-10-01T08:00:00Z', lastFetchedAt: '2026-10-15T07:30:00Z' }),
    );
    await render();

    const state = element.querySelector('.state')!.textContent!;
    expect(state).toContain('You have a calendar link, made on');
    expect(state).toContain('A calendar last fetched it on');
    expect(element.querySelector('input.link')).toBeNull();

    const confirm = vi.fn(() => false);
    vi.stubGlobal('confirm', confirm);
    button('New link')!.click();
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('The old one stops working'));
    expect(createSubscription).not.toHaveBeenCalled();

    confirm.mockReturnValue(true);
    button('New link')!.click();
    await fixture.whenStable();
    expect(createSubscription).toHaveBeenCalledOnce();
    expect(element.querySelector<HTMLInputElement>('input.link')!.value).toBe(made.url);
  });

  it('removes the link when that is confirmed, and offers to make one again', async () => {
    getSubscription.mockReturnValue(of({ ...made, url: null }));
    vi.stubGlobal('confirm', () => true);
    await render();

    button('Remove the link')!.click();
    await fixture.whenStable();

    expect(deleteSubscription).toHaveBeenCalledOnce();
    expect(success).toHaveBeenCalledWith('Your calendar link is removed.');
    expect(button('Make my calendar link')).toBeDefined();
    expect(element.querySelector('.state')).toBeNull();
  });

  it('keeps its buttons off while a change is under way', async () => {
    const answer = new Subject<CalendarSubscription>();
    createSubscription.mockReturnValue(answer);
    await render();

    button('Make my calendar link')!.click();
    await fixture.whenStable();
    expect(button('Make my calendar link')!.disabled).toBe(true);

    answer.next(made);
    answer.complete();
    await fixture.whenStable();
    expect(button('New link')!.disabled).toBe(false);
  });

  it('says so when the link could not be loaded, and tries again', async () => {
    getSubscription.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.querySelector('[role=alert]')!.textContent).toContain(
      'Your calendar link could not be loaded.',
    );
    button('Try again')!.click();
    await fixture.whenStable();

    expect(getSubscription).toHaveBeenCalledTimes(2);
    expect(element.querySelector('[role=alert]')).toBeNull();
    expect(button('Make my calendar link')).toBeDefined();
  });

  it('knows an address only this computer or its network reaches', () => {
    expect(onTheInternet('https://smartdesk.example.com/api/calendar/feed.ics')).toBe(true);
    expect(onTheInternet('https://word-word-word-word.trycloudflare.com/api/x')).toBe(true);
    expect(onTheInternet('http://localhost:4200/api/x')).toBe(false);
    expect(onTheInternet('http://127.0.0.1:4200/api/x')).toBe(false);
    expect(onTheInternet('http://[::1]:4200/api/x')).toBe(false);
    expect(onTheInternet('http://192.168.1.20:4200/api/x')).toBe(false);
    expect(onTheInternet('http://10.0.0.5:4200/api/x')).toBe(false);
    expect(onTheInternet('http://172.16.40.23:4200/api/x')).toBe(false);
    expect(onTheInternet('http://172.32.0.1/api/x')).toBe(true);
  });
});
