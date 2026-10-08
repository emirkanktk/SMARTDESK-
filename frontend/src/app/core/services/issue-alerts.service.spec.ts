import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { fakeIssue } from '../../../testing/issue-fixtures';
import { Role, Roles } from '../constants/roles';
import { IssueReport } from '../models/issue.model';
import { AuthService } from './auth.service';
import { ISSUE_TOAST_MS, IssueAlertsService } from './issue-alerts.service';
import { IssueService } from './issue.service';
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

describe('IssueAlertsService', () => {
  const isAuthenticated = signal(true);
  const passwordChangeRequired = signal(false);
  const roles = signal<Role[]>([Roles.Admin]);
  let reported: Subject<IssueReport>;
  let updated: Subject<IssueReport>;
  let reconnected: Subject<void>;
  let getAll: ReturnType<typeof vi.fn>;
  let info: ReturnType<typeof vi.fn>;
  let service: IssueAlertsService;

  beforeEach(() => {
    isAuthenticated.set(true);
    passwordChangeRequired.set(false);
    roles.set([Roles.Admin]);
    FakeNotification.permission = 'default';
    FakeNotification.shown = [];
    vi.stubGlobal('Notification', FakeNotification);
    reported = new Subject();
    updated = new Subject();
    reconnected = new Subject();
    getAll = vi.fn(() => of([fakeIssue({ id: 'a' }), fakeIssue({ id: 'b' })]));
    info = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isAuthenticated,
            passwordChangeRequired,
            hasRole: (role: Role) => roles().includes(role),
          },
        },
        {
          provide: ReservationHubService,
          useValue: { issueReported$: reported, issueUpdated$: updated, reconnected$: reconnected },
        },
        { provide: IssueService, useValue: { getAll } },
        { provide: NotificationService, useValue: { info } },
      ],
    });
    service = TestBed.inject(IssueAlertsService);
  });

  afterEach(() => vi.unstubAllGlobals());

  function start(): void {
    service.start();
    TestBed.tick();
  }

  it('counts the reports nobody has taken up yet, for those who look after resources', () => {
    start();

    expect(getAll).toHaveBeenCalledWith({ status: 'Open' });
    expect(service.open()).toBe(2);
  });

  it('counts nothing for an employee, and nothing once signed out', () => {
    roles.set([Roles.Employee]);
    start();
    expect(getAll).not.toHaveBeenCalled();
    expect(service.open()).toBe(0);

    roles.set([Roles.Manager]);
    TestBed.tick();
    expect(service.open()).toBe(2);

    isAuthenticated.set(false);
    TestBed.tick();
    expect(service.open()).toBe(0);
  });

  it('says so the moment a problem is reported, and counts again', () => {
    start();
    getAll.mockReturnValue(
      of([fakeIssue({ id: 'a' }), fakeIssue({ id: 'b' }), fakeIssue({ id: 'c' })]),
    );

    reported.next(
      fakeIssue({ resourceName: 'Board Room', category: 'AirConditioning', isBlocking: true }),
    );

    expect(info).toHaveBeenCalledWith(
      "New problem at Board Room: Air conditioning. It can't be used until it's fixed.",
      ISSUE_TOAST_MS,
    );
    expect(service.open()).toBe(3);
  });

  it('counts again when a report moves on or the connection comes back, without a toast', () => {
    start();
    getAll.mockReturnValue(of([]));

    updated.next(fakeIssue({ status: 'InProgress' }));
    expect(service.open()).toBe(0);

    getAll.mockReturnValue(of([fakeIssue()]));
    reconnected.next();
    expect(service.open()).toBe(1);
    expect(info).not.toHaveBeenCalled();
  });

  it('shows a desktop notification when allowed, which opens the list', () => {
    FakeNotification.permission = 'granted';
    start();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    reported.next(fakeIssue({ id: 'iss-9', resourceName: 'Board Room' }));

    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('Problem reported in SmartDesk');
    expect(desktop.options.body).toBe(
      "New problem at Board Room: Screen or TV. The TV doesn't turn on",
    );
    expect(desktop.options.tag).toBe('issue-iss-9');
    desktop.onclick!();
    expect(navigate).toHaveBeenCalledWith('/issues');
    expect(desktop.close).toHaveBeenCalled();
  });
});
