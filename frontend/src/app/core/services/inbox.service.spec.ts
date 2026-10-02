import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { inboxMessage } from '../../../testing/inbox-fixtures';
import { InboxMessage } from '../models/inbox.model';
import { AuthService } from './auth.service';
import { INBOX_TOAST_MS, InboxService } from './inbox.service';
import { NotificationService } from './notification.service';
import { ReservationHubService } from './reservation-hub.service';

/** Stand-in for the browser's Notification, which jsdom doesn't have. */
class FakeNotification {
  static permission: NotificationPermission = 'default';
  static shown: FakeNotification[] = [];
  onclick: (() => void) | null = null;
  close = vi.fn();

  constructor(
    readonly title: string,
    readonly options: NotificationOptions,
  ) {
    FakeNotification.shown.push(this);
  }
}

// Local times, so the text reads the same in every time zone the tests run in.
const iso = (local: string) => new Date(local).toISOString();

describe('InboxService', () => {
  const base = `${environment.apiUrl}/inbox`;
  const isAuthenticated = signal(true);
  const passwordChangeRequired = signal(false);
  let messages: Subject<InboxMessage>;
  let reconnected: Subject<void>;
  let info: ReturnType<typeof vi.fn>;
  let http: HttpTestingController;
  let service: InboxService;

  beforeEach(() => {
    isAuthenticated.set(true);
    passwordChangeRequired.set(false);
    FakeNotification.permission = 'default';
    FakeNotification.shown = [];
    vi.stubGlobal('Notification', FakeNotification);
    messages = new Subject();
    reconnected = new Subject();
    info = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { isAuthenticated, passwordChangeRequired } },
        {
          provide: ReservationHubService,
          useValue: { inboxMessage$: messages, reconnected$: reconnected },
        },
        { provide: NotificationService, useValue: { info } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(InboxService);
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  /** Starts the service signed in, with `unread` messages waiting. */
  function startWith(unread: number): void {
    service.start();
    TestBed.tick();
    http.expectOne(`${base}/unread-count`).flush({ unread });
  }

  it('counts the unread messages once signed in, and none after signing out', () => {
    startWith(3);
    expect(service.unread()).toBe(3);

    isAuthenticated.set(false);
    TestBed.tick();

    expect(service.unread()).toBe(0);
    http.expectNone(`${base}/unread-count`);
  });

  it('asks again when someone signs in', () => {
    isAuthenticated.set(false);
    service.start();
    TestBed.tick();
    http.expectNone(`${base}/unread-count`);

    isAuthenticated.set(true);
    TestBed.tick();
    http.expectOne(`${base}/unread-count`).flush({ unread: 2 });

    expect(service.unread()).toBe(2);
  });

  it('waits for a temporary password to be replaced, as the API does', () => {
    passwordChangeRequired.set(true);
    service.start();
    TestBed.tick();

    http.expectNone(`${base}/unread-count`);
    expect(service.unread()).toBe(0);
  });

  it('ignores an answer that arrives after signing out', () => {
    service.start();
    TestBed.tick();
    const pending = http.expectOne(`${base}/unread-count`);

    isAuthenticated.set(false);
    TestBed.tick();
    expect(pending.cancelled).toBe(true);
    expect(service.unread()).toBe(0);
  });

  it('counts, shows and passes on a message as it arrives', () => {
    startWith(0);
    const received: InboxMessage[] = [];
    service.received$.subscribe((message) => received.push(message));

    messages.next(inboxMessage());

    expect(service.unread()).toBe(1);
    expect(received.map((m) => m.id)).toEqual(['msg-1']);
    expect(info).toHaveBeenCalledExactlyOnceWith(
      'Zeynep Kaya invited you to a meeting: Board Room, Mon 5 Oct, 10:00-11:00.',
      INBOX_TOAST_MS,
    );
    expect(FakeNotification.shown).toEqual([]);
  });

  it('also shows it on the desktop when allowed, opening the inbox from there', () => {
    FakeNotification.permission = 'granted';
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    startWith(0);

    messages.next(inboxMessage({ id: 'msg-7', kind: 'MeetingCancelled' }));

    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('New message in SmartDesk');
    expect(desktop.options).toEqual({
      body: 'Zeynep Kaya cancelled a meeting you were invited to: Board Room, Mon 5 Oct, 10:00-11:00.',
      tag: 'inbox-msg-7',
    });
    desktop.onclick!();
    expect(navigateByUrl).toHaveBeenCalledWith('/inbox');
    expect(desktop.close).toHaveBeenCalled();
  });

  it('asks again when a message arrives while the count is on its way', () => {
    service.start();
    TestBed.tick();
    const first = http.expectOne(`${base}/unread-count`);

    messages.next(inboxMessage());
    first.flush({ unread: 4 }); // may or may not include it
    expect(service.unread()).toBe(1); // kept from the message, not the doubtful answer

    http.expectOne(`${base}/unread-count`).flush({ unread: 5 });
    expect(service.unread()).toBe(5);
  });

  it('catches up after the connection comes back', () => {
    startWith(1);

    reconnected.next();
    http.expectOne(`${base}/unread-count`).flush({ unread: 3 });

    expect(service.unread()).toBe(3);
  });

  it('keeps the count it has when asking fails', () => {
    startWith(2);

    service.refreshUnread();
    http.expectOne(`${base}/unread-count`).flush(null, { status: 500, statusText: 'Server error' });

    expect(service.unread()).toBe(2);
  });

  it('starts once, however often it is asked to', () => {
    service.start();
    service.start();
    TestBed.tick();

    http.expectOne(`${base}/unread-count`).flush({ unread: 0 });
    messages.next(inboxMessage());
    expect(info).toHaveBeenCalledTimes(1);
  });

  it('lists the messages', () => {
    let ids: string[] = [];
    service.list().subscribe((list) => (ids = list.map((m) => m.id)));

    const req = http.expectOne(base);
    expect(req.request.method).toBe('GET');
    req.flush([inboxMessage({ id: 'msg-2' }), inboxMessage()]);
    expect(ids).toEqual(['msg-2', 'msg-1']);
  });

  it('marks one read, taking it off the count only if it was unread', () => {
    startWith(2);

    service.markRead(inboxMessage()).subscribe();
    const req = http.expectOne(`${base}/msg-1/read`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(service.unread()).toBe(1);

    service.markRead(inboxMessage({ readAt: iso('2026-10-01T09:30') })).subscribe();
    http.expectOne(`${base}/msg-1/read`).flush(null, { status: 204, statusText: 'No Content' });
    expect(service.unread()).toBe(1);
  });

  it('never counts below zero', () => {
    startWith(0);

    service.markRead(inboxMessage()).subscribe();
    http.expectOne(`${base}/msg-1/read`).flush(null, { status: 204, statusText: 'No Content' });

    expect(service.unread()).toBe(0);
  });

  it('marks them all read', () => {
    startWith(4);

    service.markAllRead().subscribe();
    const req = http.expectOne(`${base}/read-all`);
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.unread()).toBe(0);
  });
});
