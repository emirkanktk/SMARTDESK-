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

// End to end through the real AuthService and both interceptors, in the order app.config
// registers them: a token that expires while the user sits on a screen must still land
// them on the login page, not leave them signed out on a screen whose calls all fail.
describe('session expiry while a screen is open', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    localStorage.setItem('smartdesk_access_token', fakeJwt());

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
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('redirects to login when a call fails with 401 after the token expired', async () => {
    const router = TestBed.inject(Router);
    const auth = TestBed.inject(AuthService);
    const backend = TestBed.inject(HttpTestingController);
    await router.navigateByUrl('/resources');
    expect(auth.isAuthenticated()).toBe(true);

    vi.setSystemTime(Date.now() + TWO_HOURS_MS);
    TestBed.inject(HttpClient)
      .get(`${environment.apiUrl}/resources`)
      .subscribe({ error: () => {} });

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({}, { status: 401, statusText: 'Unauthorized' });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login?returnUrl=%2Fresources');
    expect(TestBed.inject(NotificationService).notifications()[0]?.message).toContain(
      'session has expired',
    );
    backend.verify();
  });
});
