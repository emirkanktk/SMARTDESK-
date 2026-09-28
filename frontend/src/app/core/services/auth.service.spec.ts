import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { expiredJwt, fakeJwt } from '../../../testing/fake-jwt';
import { Roles } from '../constants/roles';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AuthService } from './auth.service';

const TOKEN_STORAGE_KEY = 'smartdesk_access_token';

describe('AuthService', () => {
  function createService(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return { service: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController) };
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('starts signed out when nothing is stored', () => {
    const { service } = createService();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
  });

  it('restores the user from a stored, unexpired token', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt({ role: [Roles.Admin, Roles.Employee] }));

    const { service } = createService();

    expect(service.isAuthenticated()).toBe(true);
    expect(service.isAdmin()).toBe(true);
    expect(service.displayName()).toBe('Ayşe Yılmaz');
  });

  it('discards a stored token that has expired', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, expiredJwt());

    const { service } = createService();

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('stores the token and derives the user on login', () => {
    const { service, http } = createService();
    const token = fakeJwt({ role: Roles.Employee });

    service.login({ email: 'employee1@smartdesk.local', password: 'secret' }).subscribe();
    http
      .expectOne({ method: 'POST', url: `${environment.apiUrl}/auth/login` })
      .flush({ accessToken: token, expiresAt: new Date().toISOString() });

    expect(service.getToken()).toBe(token);
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBe(token);
    expect(service.hasRole(Roles.Employee)).toBe(true);
    expect(service.isAdmin()).toBe(false);
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

  it('clears everything on logout', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    const { service } = createService();

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
  });
});
