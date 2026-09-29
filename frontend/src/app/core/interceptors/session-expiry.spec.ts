import { ApplicationRef, Component } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { fakeJwt } from '../../../testing/fake-jwt';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { errorInterceptor } from './error.interceptor';
import { jwtInterceptor } from './jwt.interceptor';

@Component({ template: '' })
class Blank {}

const TWO_HOURS_MS = 2 * 60 * 60 * 1000;
const RESOURCES = `${environment.apiUrl}/resources`;
const REFRESH = `${environment.apiUrl}/auth/refresh`;

// End to end through the real AuthService and both interceptors, in the order app.config
// registers them (ADR-0026). The token expires while the user sits on a screen - the laptop
// slept. If the session is still open, the next call quietly gets a new token; if it's over,
// the user lands on the login page, not on a screen whose calls all fail.
describe('session expiry while a screen is open', () => {
  let router: Router;
  let auth: AuthService;
  let backend: HttpTestingController;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: Blank },
          { path: '**', component: Blank },
        ]),
        provideHttpClient(withInterceptors([jwtInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    router = TestBed.inject(Router);
    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);

    auth.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    backend
      .expectOne(`${environment.apiUrl}/auth/login`)
      .flush({ accessToken: fakeJwt(), expiresAt: new Date().toISOString() });
    await router.navigateByUrl('/resources');
    expect(auth.isAuthenticated()).toBe(true);
    vi.setSystemTime(Date.now() + TWO_HOURS_MS);
  });

  afterEach(() => {
    backend.verify();
    vi.useRealTimers();
  });

  it('renews the token and carries on when the session is still open', async () => {
    let body: unknown;
    TestBed.inject(HttpClient)
      .get(RESOURCES)
      .subscribe((b) => (body = b));

    const renewed = fakeJwt({ given_name: 'Renewed' });
    await vi.waitFor(() =>
      backend
        .expectOne(REFRESH)
        .flush({ accessToken: renewed, expiresAt: new Date().toISOString() }),
    );
    const req = await vi.waitFor(() => backend.expectOne(RESOURCES));
    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${renewed}`);
    req.flush([]);

    expect(body).toEqual([]);
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.url).toBe('/resources');
  });

  it('redirects to login when the session is over too', async () => {
    TestBed.inject(HttpClient)
      .get(RESOURCES)
      .subscribe({ error: () => {} });

    // The expired token can't be renewed, so the call goes out without one...
    await vi.waitFor(() =>
      backend.expectOne(REFRESH).flush(null, { status: 401, statusText: 'Unauthorized' }),
    );
    const req = await vi.waitFor(() => backend.expectOne(RESOURCES));
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({}, { status: 401, statusText: 'Unauthorized' });
    // ...the 401 tries one more renewal, which fails too, and the user is signed out.
    await vi.waitFor(() =>
      backend.expectOne(REFRESH).flush(null, { status: 401, statusText: 'Unauthorized' }),
    );
    await vi.waitFor(() =>
      backend
        .expectOne(`${environment.apiUrl}/auth/logout`)
        .flush(null, { status: 204, statusText: 'No Content' }),
    );
    await TestBed.inject(ApplicationRef).whenStable();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login?returnUrl=%2Fresources');
    expect(TestBed.inject(NotificationService).notifications()[0]?.message).toContain(
      'session has expired',
    );
  });
});
