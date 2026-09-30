import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { expiredJwt, fakeJwt } from '../../../testing/fake-jwt';
import { Roles } from '../constants/roles';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AuthService } from './auth.service';

const REFRESH = `${environment.apiUrl}/auth/refresh`;
const LOGIN = `${environment.apiUrl}/auth/login`;
const LOGOUT = `${environment.apiUrl}/auth/logout`;
const SIGN_OUT_PENDING = 'smartdesk_sign_out_pending';

describe('AuthService', () => {
  let otherTab: BroadcastChannel;

  function createService(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return { service: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController) };
  }

  /** Lets a renewal's promise, and messages from other tabs, arrive. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  const session = (overrides: Record<string, unknown> = {}) => ({
    accessToken: fakeJwt({ role: Roles.Employee }),
    expiresAt: new Date().toISOString(),
    passwordChangeRequired: false,
    ...overrides,
  });

  beforeEach(() => {
    localStorage.clear();
    otherTab = new BroadcastChannel('smartdesk-session');
  });

  afterEach(() => {
    otherTab.close();
    localStorage.clear();
    vi.useRealTimers();
  });

  it('starts signed out, and stays so when the browser has no session', async () => {
    const { service, http } = createService();
    expect(service.isAuthenticated()).toBe(false);

    const restored = service.restoreSession();
    const req = http.expectOne(REFRESH);
    expect(req.request.method).toBe('POST');
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(null, { status: 401, statusText: 'Unauthorized' });
    await restored;

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
  });

  it('takes up the session this browser already has', async () => {
    const { service, http } = createService();
    const token = fakeJwt({ role: [Roles.Admin, Roles.Employee] });

    const restored = service.restoreSession();
    http.expectOne(REFRESH).flush(session({ accessToken: token }));
    await restored;

    expect(service.getToken()).toBe(token);
    expect(service.isAdmin()).toBe(true);
    expect(service.displayName()).toBe('Ayşe Yılmaz');
  });

  it('keeps the token in memory only, and clears what older versions stored', () => {
    localStorage.setItem('smartdesk_access_token', fakeJwt());
    localStorage.setItem('smartdesk_password_change_required', 'true');
    const { service, http } = createService();
    const token = fakeJwt({ role: Roles.Employee });

    expect(localStorage.getItem('smartdesk_access_token')).toBeNull();
    expect(localStorage.getItem('smartdesk_password_change_required')).toBeNull();

    service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    const req = http.expectOne({ method: 'POST', url: LOGIN });
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    req.flush(session({ accessToken: token }));

    expect(service.getToken()).toBe(token);
    expect(service.hasRole(Roles.Employee)).toBe(true);
    expect(service.isAdmin()).toBe(false);
    expect(Object.values(localStorage)).not.toContain(token);
  });

  it('treats an expired token as none', () => {
    const { service, http } = createService();

    service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    http.expectOne(LOGIN).flush(session({ accessToken: expiredJwt() }));

    expect(service.isAuthenticated()).toBe(false);
    expect(service.hasValidSession()).toBe(false);
  });

  it('knows from the server that a temporary password must be changed, even after a reload', async () => {
    const { service, http } = createService();

    const restored = service.restoreSession();
    http.expectOne(REFRESH).flush(session({ passwordChangeRequired: true }));
    await restored;

    expect(service.passwordChangeRequired()).toBe(true);
  });

  it('marks a pending password change when the API asks for one', () => {
    const { service } = createService();

    service.markPasswordChangeRequired();

    expect(service.passwordChangeRequired()).toBe(true);
  });

  it('changes the password, keeps the new token and forgets the pending change', () => {
    const { service, http } = createService();
    service.markPasswordChangeRequired();
    const newToken = fakeJwt({ given_name: 'Renewed' });

    service
      .changePassword({ currentPassword: 'Temp-Pass-2026', newPassword: 'Own-Pass-2026' })
      .subscribe();
    const req = http.expectOne(`${environment.apiUrl}/auth/change-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      currentPassword: 'Temp-Pass-2026',
      newPassword: 'Own-Pass-2026',
    });
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(false);
    req.flush(session({ accessToken: newToken }));

    expect(service.getToken()).toBe(newToken);
    expect(service.passwordChangeRequired()).toBe(false);
  });

  it('signs out on the server too, and clears everything here', () => {
    const { service, http } = createService();
    service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    http.expectOne(LOGIN).flush(session());

    service.logout();

    const req = http.expectOne({ method: 'POST', url: LOGOUT });
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
    expect(service.passwordChangeRequired()).toBe(false);
    expect(localStorage.getItem(SIGN_OUT_PENDING)).toBeNull();
  });

  describe('a sign-out that cannot reach the server', () => {
    // The session, and its HttpOnly cookie, would still be there on the next visit: on a shared
    // computer the next person would find themselves signed in.
    function signOutOffline(): void {
      const { service, http } = createService();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session());
      service.logout();
      http.expectOne(LOGOUT).error(new ProgressEvent('error'));
      expect(service.isAuthenticated()).toBe(false);
      TestBed.resetTestingModule();
    }

    it('is finished on the next visit, before any session is taken up', async () => {
      signOutOffline();
      expect(localStorage.getItem(SIGN_OUT_PENDING)).not.toBeNull();

      const { service, http } = createService();
      const restored = service.restoreSession();
      await vi.waitFor(() =>
        http.expectOne(LOGOUT).flush(null, { status: 204, statusText: 'No Content' }),
      );
      await restored;

      http.expectNone(REFRESH);
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(SIGN_OUT_PENDING)).toBeNull();
    });

    it('keeps the session from being taken up while the server still cannot be reached', async () => {
      signOutOffline();

      const { service, http } = createService();
      const restored = service.restoreSession();
      await vi.waitFor(() => http.expectOne(LOGOUT).error(new ProgressEvent('error')));
      await restored;

      http.expectNone(REFRESH);
      expect(service.isAuthenticated()).toBe(false);
      expect(localStorage.getItem(SIGN_OUT_PENDING)).not.toBeNull();
    });

    it('is forgotten once someone signs in again', () => {
      signOutOffline();

      const { service, http } = createService();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session());

      expect(localStorage.getItem(SIGN_OUT_PENDING)).toBeNull();
    });
  });

  describe('renewing', () => {
    it('hands out the current token while it has time left', async () => {
      const { service, http } = createService();
      const token = fakeJwt();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session({ accessToken: token }));

      let handed: string | null = null;
      service.validToken().subscribe((t) => (handed = t));

      expect(handed).toBe(token);
      http.expectNone(REFRESH);
    });

    it('renews a token about to expire, once for everyone asking', async () => {
      const { service, http } = createService();
      const soon = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 10 });
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session({ accessToken: soon }));
      const renewed = fakeJwt({ given_name: 'Renewed' });

      const handed: (string | null)[] = [];
      service.validToken().subscribe((t) => handed.push(t));
      service.validToken().subscribe((t) => handed.push(t));
      http.expectOne(REFRESH).flush(session({ accessToken: renewed }));
      await settle();

      expect(handed).toEqual([renewed, renewed]);
      expect(service.getToken()).toBe(renewed);
    });

    it('signs out here when the session is over', async () => {
      const { service, http } = createService();
      const soon = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 10 });
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session({ accessToken: soon }));

      let handed: string | null = 'unset';
      service.validToken().subscribe((t) => (handed = t));
      http.expectOne(REFRESH).flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();

      expect(handed).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
    });

    it('renews on its own a minute before the token expires', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      const { service, http } = createService();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      // fakeJwt expires in an hour.
      http.expectOne(LOGIN).flush(session());

      await vi.advanceTimersByTimeAsync(58 * 60_000);
      http.expectNone(REFRESH);

      await vi.advanceTimersByTimeAsync(60_000);
      http.expectOne(REFRESH).flush(session());
      await vi.advanceTimersByTimeAsync(0);

      expect(service.isAuthenticated()).toBe(true);
    });

    it('stays signed in when the renewal gets no answer, and tries again, less and less often', async () => {
      // A laptop waking before its Wi-Fi: the session is still open on the server.
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      const { service, http } = createService();
      const current = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 5 * 60 });
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session({ accessToken: current }));

      await vi.advanceTimersByTimeAsync(4 * 60_000);
      http.expectOne(REFRESH).error(new ProgressEvent('error'));
      await vi.advanceTimersByTimeAsync(0);
      expect(service.isAuthenticated()).toBe(true);
      expect(service.getToken()).toBe(current);

      // Again after 10 s, then 20 s, then 40 s.
      for (const wait of [10_000, 20_000, 40_000]) {
        await vi.advanceTimersByTimeAsync(wait - 1);
        http.expectNone(REFRESH);
        await vi.advanceTimersByTimeAsync(1);
        http.expectOne(REFRESH).flush(null, { status: 503, statusText: 'Service Unavailable' });
        await vi.advanceTimersByTimeAsync(0);
      }
      // The token has expired meanwhile, but nobody was signed out.
      expect(service.getToken()).toBeNull();
      expect(service.isAuthenticated()).toBe(true);

      // Then every minute, until an answer comes.
      await vi.advanceTimersByTimeAsync(60_000);
      const renewed = fakeJwt({ given_name: 'Renewed' });
      http.expectOne(REFRESH).flush(session({ accessToken: renewed }));
      await vi.advanceTimersByTimeAsync(0);
      expect(service.getToken()).toBe(renewed);

      // Back to the usual renewal, a minute before the new token expires.
      await vi.advanceTimersByTimeAsync(60_000);
      http.expectNone(REFRESH);
    });

    it('leaves a guest alone when the renewal at start-up gets no answer', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      const { service, http } = createService();

      const restored = service.restoreSession();
      http.expectOne(REFRESH).error(new ProgressEvent('error'));
      await vi.advanceTimersByTimeAsync(0);
      await restored;

      expect(service.isAuthenticated()).toBe(false);
      await vi.advanceTimersByTimeAsync(5 * 60_000);
      http.expectNone(REFRESH);
    });

    it('signs out here when the cookie is gone (a 204 to the renewal)', async () => {
      // Say the browser's site data was cleared: there's no session left to renew.
      const { service, http } = createService();
      const soon = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 10 });
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session({ accessToken: soon }));

      service.validToken().subscribe();
      http.expectOne(REFRESH).flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(service.isAuthenticated()).toBe(false);
    });

    it('takes a 204 to mean the browser has no session', async () => {
      const { service, http } = createService();

      const restored = service.restoreSession();
      http.expectOne(REFRESH).flush(null, { status: 204, statusText: 'No Content' });
      await restored;

      expect(service.isAuthenticated()).toBe(false);
      expect(service.getToken()).toBeNull();
    });
  });

  describe('with other tabs', () => {
    it('signs out here when another tab signs out', async () => {
      const { service, http } = createService();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session());

      otherTab.postMessage('signed-out');
      await vi.waitFor(() => expect(service.isAuthenticated()).toBe(false));
      http.expectNone(LOGOUT);
    });

    it('takes up the new session when another tab signs in', async () => {
      const { service, http } = createService();
      const theirs = fakeJwt({ given_name: 'Mehmet', family_name: 'Demir' });

      otherTab.postMessage('signed-in');
      await vi.waitFor(() => http.expectOne(REFRESH).flush(session({ accessToken: theirs })));
      await settle();

      expect(service.displayName()).toBe('Mehmet Demir');
    });

    it('tells the other tabs when this one signs in or out', async () => {
      const heard: unknown[] = [];
      otherTab.onmessage = (event) => heard.push(event.data);
      const { service, http } = createService();

      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session());
      service.logout();
      http.expectOne(LOGOUT).flush(null);

      await vi.waitFor(() => expect(heard).toEqual(['signed-in', 'signed-out']));
    });

    it('stops listening once destroyed', async () => {
      const { service, http } = createService();
      service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
      http.expectOne(LOGIN).flush(session());

      TestBed.resetTestingModule();
      otherTab.postMessage('signed-out');
      await settle();

      expect(service.isAuthenticated()).toBe(true);
    });
  });

  it('changes the name, shows it at once, then renews so the token carries it', async () => {
    const { service, http } = createService();
    service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    http.expectOne(LOGIN).flush(session());

    let saved: unknown;
    service
      .updateProfile({ firstName: 'Ayşe Nur', lastName: 'Demir' })
      .subscribe((u) => (saved = u));
    const req = http.expectOne({ method: 'PUT', url: `${environment.apiUrl}/auth/me` });
    expect(req.request.body).toEqual({ firstName: 'Ayşe Nur', lastName: 'Demir' });
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    const profile = {
      id: '11111111-1111-1111-1111-111111111111',
      firstName: 'Ayşe Nur',
      lastName: 'Demir',
      email: 'employee1@smartdesk.local',
      roles: [Roles.Employee],
    };
    req.flush(profile);

    expect(service.displayName()).toBe('Ayşe Nur Demir');
    const renewed = fakeJwt({ given_name: 'Ayşe Nur', family_name: 'Demir' });
    http.expectOne(REFRESH).flush(session({ accessToken: renewed }));
    await settle();

    expect(saved).toEqual(profile);
    expect(service.getToken()).toBe(renewed);
    expect(service.displayName()).toBe('Ayşe Nur Demir');
  });

  it('asks for a reset link without a session and without a toast', () => {
    const { service, http } = createService();

    service.forgotPassword('ayse@smartdesk.local').subscribe();

    const req = http.expectOne(`${environment.apiUrl}/auth/forgot-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'ayse@smartdesk.local' });
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(null, { status: 202, statusText: 'Accepted' });
  });

  it('resets the password with the token from the link', () => {
    const { service, http } = createService();
    const body = { email: 'ayse@smartdesk.local', token: 'abc-_1', newPassword: 'New-Pass-7' };

    service.resetPassword(body).subscribe();

    const req = http.expectOne(`${environment.apiUrl}/auth/reset-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    req.flush(null, { status: 204, statusText: 'No Content' });
    http.verify();
  });

  it('signs up without a session and without a toast', () => {
    const { service, http } = createService();
    const body = {
      firstName: 'Zeynep',
      lastName: 'Kaya',
      email: 'zeynep@company.example',
      password: 'Team-Lead-Pass-4',
    };

    service.register(body).subscribe();

    const req = http.expectOne(`${environment.apiUrl}/auth/register`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(null, { status: 202, statusText: 'Accepted' });
    http.verify();
  });
});
