import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../environments/environment';
import { fakeJwt } from '../testing/fake-jwt';
import { App } from './app';
import { AuthService } from './core/services/auth.service';
import { InboxService } from './core/services/inbox.service';
import { IssueAlertsService } from './core/services/issue-alerts.service';
import { ReminderAlertsService } from './core/services/reminder-alerts.service';
import { ReservationHubService } from './core/services/reservation-hub.service';

describe('App', () => {
  const hub = {
    connect: vi.fn(),
    disconnect: vi.fn(),
  };
  const reminderAlerts = { start: vi.fn() };
  const inbox = { start: vi.fn() };
  const issueAlerts = { start: vi.fn() };

  beforeEach(async () => {
    localStorage.clear();
    hub.connect.mockClear();
    hub.disconnect.mockClear();
    reminderAlerts.start.mockClear();
    inbox.start.mockClear();
    issueAlerts.start.mockClear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ReservationHubService, useValue: hub },
        { provide: ReminderAlertsService, useValue: reminderAlerts },
        { provide: InboxService, useValue: inbox },
        { provide: IssueAlertsService, useValue: issueAlerts },
      ],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  function signIn(passwordChangeRequired = false): void {
    TestBed.inject(AuthService)
      .login({ email: 'employee1@smartdesk.local', password: 'secret' })
      .subscribe();
    TestBed.inject(HttpTestingController).expectOne(`${environment.apiUrl}/auth/login`).flush({
      accessToken: fakeJwt(),
      expiresAt: new Date().toISOString(),
      passwordChangeRequired,
    });
  }

  it('starts showing reservation reminders, inbox messages and reported problems', () => {
    TestBed.createComponent(App).detectChanges();

    expect(reminderAlerts.start).toHaveBeenCalledTimes(1);
    expect(inbox.start).toHaveBeenCalledTimes(1);
    expect(issueAlerts.start).toHaveBeenCalledTimes(1);
  });

  it('disconnects the hub when nobody is signed in', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(hub.disconnect).toHaveBeenCalled();
    expect(hub.connect).not.toHaveBeenCalled();
  });

  it('connects the hub on sign-in and disconnects it on sign-out', () => {
    signIn();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(hub.connect).toHaveBeenCalled();

    TestBed.inject(AuthService).logout();
    fixture.detectChanges();

    expect(hub.disconnect).toHaveBeenCalled();
  });

  it('waits for a temporary password to be replaced before connecting', () => {
    signIn(true);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(hub.connect).not.toHaveBeenCalled();
    expect(hub.disconnect).toHaveBeenCalled();
  });
});
