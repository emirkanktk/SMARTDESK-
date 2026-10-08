import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { INBOX_PAGE_SIZE, InboxMessage } from '../../core/models/inbox.model';
import { AttendeeResponse, Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { InboxService } from '../../core/services/inbox.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { inboxMessage } from '../../../testing/inbox-fixtures';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { Inbox } from './inbox';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('Inbox', () => {
  const NOW = new Date('2026-10-01T15:00'); // local time

  const invitation = inboxMessage({ id: 'm-5', createdAt: utc('2026-10-01T14:30') });
  const changed = inboxMessage({
    id: 'm-4',
    kind: 'MeetingChanged',
    resourceName: 'Meeting Room A',
    startDateTime: utc('2026-10-05T22:00'),
    endDateTime: utc('2026-10-06T01:00'),
    createdAt: utc('2026-09-29T08:15'),
    readAt: utc('2026-09-29T09:00'),
  });
  const cancelled = inboxMessage({
    id: 'm-3',
    kind: 'MeetingCancelled',
    reservationId: 'rsv-2',
    createdAt: utc('2026-09-20T10:00'),
  });
  const released = inboxMessage({
    id: 'm-2',
    kind: 'MeetingCancelled',
    reservationId: 'rsv-3',
    senderId: null,
    senderName: null,
    createdAt: utc('2026-09-10T10:15'),
  });
  const uninvited = inboxMessage({
    id: 'm-1',
    kind: 'MeetingUninvited',
    reservationId: 'rsv-4',
    senderName: null, // the account is gone
    createdAt: utc('2025-12-30T12:00'),
  });

  /** Board Room, which Ayşe (the reader) is invited to and hasn't answered yet. */
  const meeting = (response: AttendeeResponse = 'Pending', extra: Partial<Reservation> = {}) =>
    fakeReservation({
      id: 'rsv-1',
      userId: 'u-zeynep',
      userDisplayName: 'Zeynep Kaya',
      resourceName: 'Board Room',
      startDateTime: utc('2026-10-05T10:00'),
      endDateTime: utc('2026-10-05T11:00'),
      attendees: [{ id: 'u-ayse', name: 'Ayşe Yılmaz', response }],
      ...extra,
    });

  const unread = signal(0);
  let received: Subject<InboxMessage>;
  let getInvited: ReturnType<typeof vi.fn>;
  let respond: ReturnType<typeof vi.fn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
  };
  let list: ReturnType<typeof vi.fn>;
  let refreshUnread: ReturnType<typeof vi.fn>;
  let markRead: ReturnType<typeof vi.fn>;
  let markAllRead: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Inbox>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    unread.set(2);
    received = new Subject();
    list = vi.fn(() => of([invitation, changed, cancelled, released, uninvited]));
    refreshUnread = vi.fn();
    markRead = vi.fn(() => of(undefined));
    markAllRead = vi.fn(() => of(undefined));
    getInvited = vi.fn(() => of([meeting()]));
    respond = vi.fn((_: string, body: { response: AttendeeResponse }) =>
      of(meeting(body.response)),
    );
    hub = { created$: new Subject(), updated$: new Subject(), cancelled$: new Subject() };

    await TestBed.configureTestingModule({
      imports: [Inbox],
      providers: [
        provideRouter([]),
        {
          provide: InboxService,
          useValue: { unread, received$: received, list, refreshUnread, markRead, markAllRead },
        },
        { provide: ReservationService, useValue: { getInvited, respond } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser: signal({ id: 'u-ayse' }) } },
        { provide: NotificationService, useValue: { success: vi.fn() } },
      ],
    }).compileComponents();
  });

  afterEach(() => vi.useRealTimers());

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Inbox);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const messages = () => Array.from(element.querySelectorAll<HTMLElement>('.message'));
  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  const headlines = () =>
    messages().map((m) => text(m.querySelector('.headline')!).replace(/^Unread: /, ''));

  function button(message: HTMLElement, label: string): HTMLButtonElement | undefined {
    return Array.from(message.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent!.includes(label),
    );
  }

  it('says what happened in each message, newest first', async () => {
    await render();

    expect(headlines()).toEqual([
      'Zeynep Kaya invited you to a meeting',
      "Zeynep Kaya changed a meeting you're invited to",
      'Zeynep Kaya cancelled a meeting you were invited to',
      'A meeting you were invited to was released because nobody checked in',
      'Someone removed you from a meeting',
    ]);
  });

  it('shows the meeting as it was when the message was sent, and when it arrived', async () => {
    await render();
    const [first, second, , , last] = messages();

    expect(text(first.querySelector('.meeting')!)).toBe('Board Room · Mon 5 Oct, 10:00-11:00');
    expect(text(first.querySelector('.note')!)).toBe('Sprint planning');
    expect(text(first.querySelector('.received')!)).toBe('14:30'); // today
    expect(text(second.querySelector('.meeting')!)).toBe(
      'Meeting Room A · Mon 5 Oct, 22:00 to Tue 6 Oct, 01:00',
    );
    expect(text(second.querySelector('.received')!)).toBe('Tue 29 Sep, 08:15');
    expect(text(last.querySelector('.received')!)).toBe('30 Dec 2025'); // last year
    expect(last.querySelector('.note')).toBeNull(); // not theirs any more
  });

  it('tells unread messages apart, for the eye and for a screen reader', async () => {
    await render();
    const [first, second] = messages();

    expect(first.classList).toContain('unread');
    expect(text(first.querySelector('.headline')!)).toMatch(/^Unread: /);
    expect(second.classList).not.toContain('unread');
    expect(button(second, 'Mark as read')).toBeUndefined();
  });

  it('marks a message read', async () => {
    await render();
    button(messages()[0], 'Mark as read')!.click();
    await fixture.whenStable();

    expect(markRead).toHaveBeenCalledExactlyOnceWith(invitation);
    expect(messages()[0].classList).not.toContain('unread');
    expect(button(messages()[0], 'Mark as read')).toBeUndefined();
  });

  it('keeps a message unread when marking it failed', async () => {
    markRead.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
    await render();
    button(messages()[0], 'Mark as read')!.click();
    await fixture.whenStable();

    expect(messages()[0].classList).toContain('unread');
    expect(button(messages()[0], 'Mark as read')!.disabled).toBe(false);
  });

  it('opens the meeting from news of one still on, marking the message read', async () => {
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await render();
    const links = messages().map(
      (m) => m.querySelector<HTMLAnchorElement>('a')?.getAttribute('href') ?? null,
    );

    expect(links).toEqual([
      '/reservations/rsv-1/edit',
      '/reservations/rsv-1/edit',
      null,
      null,
      null,
    ]);
    messages()[0].querySelector<HTMLAnchorElement>('a')!.click();
    expect(markRead).toHaveBeenCalledWith(invitation);
    expect(navigateByUrl).toHaveBeenCalled();
  });

  it('marks them all read, and then offers that no more', async () => {
    await render();
    const markAll = element.querySelector<HTMLButtonElement>('.mark-all')!;
    expect(markAll.textContent!.trim()).toBe('Mark all as read');

    unread.set(0); // the service counts them off
    markAll.click();
    await fixture.whenStable();

    expect(markAllRead).toHaveBeenCalled();
    expect(messages().filter((m) => m.classList.contains('unread'))).toEqual([]);
    expect(element.querySelector('.mark-all')).toBeNull();
  });

  it('offers to mark them all read while the count says some are unread elsewhere', async () => {
    list.mockReturnValue(of([changed]));
    unread.set(1);
    await render();
    expect(element.querySelector('.mark-all')).not.toBeNull();

    unread.set(0);
    await fixture.whenStable();
    expect(element.querySelector('.mark-all')).toBeNull();
  });

  it('puts a message that arrives on top', async () => {
    await render();
    received.next(inboxMessage({ id: 'm-6', kind: 'MeetingChanged', resourceName: 'Studio' }));
    await fixture.whenStable();

    expect(headlines()[0]).toBe("Zeynep Kaya changed a meeting you're invited to");
    expect(messages()).toHaveLength(6);
  });

  it('keeps a message that arrives while the list is loading, once', async () => {
    const response = new Subject<InboxMessage[]>();
    list.mockReturnValue(response);
    await render();

    const early = inboxMessage({ id: 'm-6' });
    received.next(early);
    response.next([early, changed]); // this answer has it too
    response.complete();
    await fixture.whenStable();

    expect(messages()).toHaveLength(2);
  });

  it('brings the unread count up to date when opened', async () => {
    await render();

    expect(refreshUnread).toHaveBeenCalledTimes(1);
  });

  it('says so when there are no messages', async () => {
    list.mockReturnValue(of([]));
    unread.set(0);
    await render();

    expect(element.textContent).toContain('No messages yet.');
    expect(element.querySelector('.mark-all')).toBeNull();
  });

  it('offers a retry when loading fails', async () => {
    list.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain('Your messages could not be loaded.');
    expect(element.querySelector('.mark-all')).toBeNull();
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(messages()).toHaveLength(5);
  });

  describe('answering an invitation (ADR-0032)', () => {
    const answers = () => messages().map((m) => m.querySelector('app-meeting-answer') !== null);

    function button(label: string): HTMLButtonElement {
      return Array.from(
        element.querySelectorAll<HTMLButtonElement>('app-meeting-answer button'),
      ).find((b) => b.textContent!.trim() === label)!;
    }

    it('asks on the newest message about a meeting the reader is still invited to', async () => {
      const update = inboxMessage({
        id: 'm-6',
        kind: 'MeetingChanged',
        createdAt: utc('2026-10-01T14:45'),
      });
      list.mockReturnValue(of([update, invitation, changed, cancelled]));
      await render();

      expect(answers()).toEqual([true, false, false, false]);
      expect(text(messages()[0])).toContain('Will you come?');
    });

    it('answers from the message, which marks it read', async () => {
      await render();
      button("I'm coming").click();
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledExactlyOnceWith('rsv-1', { response: 'Accepted' });
      expect(markRead).toHaveBeenCalledWith(invitation);
      expect(text(messages()[0])).toContain("You said you're coming.");
    });

    it('says no with a reason', async () => {
      await render();
      button("I can't come").click();
      await fixture.whenStable();
      const textarea = element.querySelector('textarea')!;
      textarea.value = 'Off sick';
      textarea.dispatchEvent(new Event('input'));
      element.querySelector('app-meeting-answer form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-1', { response: 'Declined', reason: 'Off sick' });
    });

    it("doesn't ask about a meeting the reader is no longer invited to", async () => {
      getInvited.mockReturnValue(of([]));
      await render();

      expect(answers()).toEqual([false, false, false, false, false]);
    });

    it('stops asking once the meeting is cancelled or they are taken off it', async () => {
      await render();
      hub.cancelled$.next(meeting('Pending', { status: 'Cancelled' }));
      await fixture.whenStable();
      expect(answers()[0]).toBe(false);

      hub.updated$.next(meeting());
      await fixture.whenStable();
      expect(answers()[0]).toBe(true);

      hub.updated$.next(meeting('Pending', { attendees: [] }));
      await fixture.whenStable();
      expect(answers()[0]).toBe(false);
    });

    it('still shows the messages when the invitations could not be loaded', async () => {
      getInvited.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render();

      expect(messages()).toHaveLength(5);
      expect(answers().some(Boolean)).toBe(false);
    });

    it('asks on news that the meeting runs longer, too', async () => {
      list.mockReturnValue(of([inboxMessage({ id: 'm-9', kind: 'MeetingExtended' }), invitation]));
      await render();

      expect(headlines()[0]).toBe("Zeynep Kaya extended a meeting you're invited to");
      expect(answers()).toEqual([true, false]);
    });

    it('tells the organizer who is coming, and who can not and why', async () => {
      list.mockReturnValue(
        of([
          inboxMessage({
            id: 'm-8',
            kind: 'MeetingDeclined',
            senderName: 'Mehmet Demir',
            reason: 'At the dentist',
          }),
          inboxMessage({ id: 'm-7', kind: 'MeetingAccepted', senderName: 'Ayşe Yılmaz' }),
        ]),
      );
      getInvited.mockReturnValue(of([]));
      await render();
      const [declined, accepted] = messages();

      expect(headlines()).toEqual([
        "Mehmet Demir can't come to your meeting",
        'Ayşe Yılmaz is coming to your meeting',
      ]);
      expect(text(declined.querySelector('.note')!)).toBe('At the dentist');
      expect(accepted.querySelector('.note')).toBeNull(); // not the meeting's note
      expect(declined.querySelector('a')!.getAttribute('href')).toBe('/reservations/rsv-1/edit');
    });
  });

  it('says that only the latest are shown once the list is full', async () => {
    const many = Array.from({ length: INBOX_PAGE_SIZE }, (_, i) =>
      inboxMessage({ id: `m-${i}`, readAt: utc('2026-10-01T09:00') }),
    );
    list.mockReturnValue(of(many));
    await render();

    expect(element.textContent).toContain('Only your latest messages are shown.');
  });

  it('says nothing about that for a shorter list', async () => {
    await render();

    expect(element.textContent).not.toContain('Only your latest messages are shown.');
  });

  it('tells everyone on a booking that it was cancelled because its resource closed, and why (ADR-0038)', async () => {
    list.mockReturnValue(
      of([
        inboxMessage({
          id: 'm-block',
          kind: 'CancelledByBlock',
          senderName: 'Selin Kaya',
          reason: 'Air conditioning: too hot to sit in',
        }),
      ]),
    );
    await render();
    const [message] = messages();

    expect(headlines()).toEqual([
      "Selin Kaya closed a resource, so a booking you're on was cancelled",
    ]);
    expect(text(message.querySelector('.meeting')!)).toBe('Board Room · Mon 5 Oct, 10:00-11:00');
    expect(text(message.querySelector('.note')!)).toBe('Air conditioning: too hot to sit in');
    expect(message.querySelector('app-meeting-answer')).toBeNull();
  });

  it('tells a reporter their problem is resolved, with what it was and their own words (ADR-0037)', async () => {
    list.mockReturnValue(
      of([
        inboxMessage({
          id: 'm-issue',
          kind: 'IssueResolved',
          reservationId: null,
          resourceName: 'Board Room',
          startDateTime: utc('2026-10-05T09:30'),
          endDateTime: utc('2026-10-05T11:00'),
          note: "The TV doesn't turn on",
          issueCategory: 'Display',
        }),
      ]),
    );
    await render();
    const [message] = messages();

    expect(headlines()).toEqual(['The problem you reported is resolved']);
    expect(text(message.querySelector('.meeting')!)).toBe(
      'Board Room · Screen or TV, reported Mon 5 Oct, 09:30',
    );
    expect(text(message.querySelector('.note')!)).toBe("The TV doesn't turn on");
    expect(message.querySelector('a')).toBeNull(); // no meeting to open
    expect(message.querySelector('app-meeting-answer')).toBeNull();
  });
  describe('a meeting that repeats (ADR-0035)', () => {
    const series = {
      id: 'series-1',
      frequency: 'Weekly' as const,
      interval: 1,
      daysOfWeek: ['Monday' as const],
      until: '2026-10-26',
      timeZone: 'Europe/Istanbul',
    };
    const seriesInvitation = inboxMessage({
      id: 'm-9',
      kind: 'SeriesInvitation',
      series,
      createdAt: utc('2026-10-01T14:45'),
    });
    /** A date of the series Ayşe is invited to. */
    const date = (id: string, day: string, response: AttendeeResponse = 'Pending') =>
      meeting(response, {
        id,
        series,
        startDateTime: utc(`2026-10-${day}T10:00`),
        endDateTime: utc(`2026-10-${day}T11:00`),
      });

    it('says it repeats, from when, how and until when', async () => {
      list.mockReturnValue(of([seriesInvitation]));
      await render();
      const [message] = messages();

      expect(headlines()).toEqual(['Zeynep Kaya invited you to a meeting that repeats']);
      expect(text(message.querySelector('.meeting')!)).toBe(
        'Board Room · from Mon 5 Oct, every Monday until 26 Oct 2026, 10:00-11:00',
      );
    });

    it('is answered from its next date, for that date and every later one', async () => {
      list.mockReturnValue(of([seriesInvitation]));
      // The first date is over and gone from the invitations; the next ones are still to come.
      getInvited.mockReturnValue(of([date('rsv-19', '19'), date('rsv-12', '12')]));
      respond.mockReturnValue(of(date('rsv-12', '12', 'Accepted')));
      await render();
      const [message] = messages();

      expect(message.textContent).toContain('Your answer is for this date and every later one.');
      button(message, "I'm coming")!.click();
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-12', { response: 'Accepted', following: true });
      expect(message.querySelector('a')!.getAttribute('href')).toBe('/reservations/rsv-12/edit');
    });

    it("tells the organizer who can't come from a date on, and why", async () => {
      list.mockReturnValue(
        of([
          inboxMessage({
            id: 'm-10',
            kind: 'SeriesDeclined',
            senderName: 'Ayşe Yılmaz',
            reason: 'Training on Mondays',
            series,
          }),
          inboxMessage({ id: 'm-11', kind: 'SeriesCancelled', series, note: null }),
          inboxMessage({ id: 'm-12', kind: 'SeriesAccepted', senderName: 'Mehmet Demir', series }),
        ]),
      );
      getInvited.mockReturnValue(of([]));
      await render();
      const [declined] = messages();

      expect(headlines()).toEqual([
        "Ayşe Yılmaz can't come to your meeting that repeats",
        'Zeynep Kaya cancelled the rest of a meeting that repeats',
        'Mehmet Demir is coming to your meeting that repeats',
      ]);
      expect(text(declined.querySelector('.note')!)).toBe('Training on Mondays');
      expect(element.querySelector('app-meeting-answer')).toBeNull();
    });
  });
});
