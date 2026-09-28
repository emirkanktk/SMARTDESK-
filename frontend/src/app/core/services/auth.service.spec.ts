import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { expiredJwt, fakeJwt } from '../../../testing/fake-jwt';
import { Roles } from '../constants/roles';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AuthService } from './auth.service';

const TOKEN_STORAGE_KEY = 'smartdesk_access_token';
const PASSWORD_CHANGE_STORAGE_KEY = 'smartdesk_password_change_required';

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

  it('remembers, across a reload, that a temporary password must be changed', () => {
    const { service, http } = createService();

    service.login({ email: 'elif@smartdesk.local', password: 'Temp-Pass-2026' }).subscribe();
    http.expectOne(`${environment.apiUrl}/auth/login`).flush({
      accessToken: fakeJwt(),
      expiresAt: new Date().toISOString(),
      passwordChangeRequired: true,
    });

    expect(service.passwordChangeRequired()).toBe(true);
    TestBed.resetTestingModule();
    expect(createService().service.passwordChangeRequired()).toBe(true);
  });

  it('changes the password, keeps the new token and forgets the pending change', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    localStorage.setItem(PASSWORD_CHANGE_STORAGE_KEY, 'true');
    const { service, http } = createService();
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
    req.flush({
      accessToken: newToken,
      expiresAt: new Date().toISOString(),
      passwordChangeRequired: false,
    });

    expect(service.getToken()).toBe(newToken);
    expect(service.passwordChangeRequired()).toBe(false);
    expect(localStorage.getItem(PASSWORD_CHANGE_STORAGE_KEY)).toBeNull();
  });

  it('marks a pending password change when the API asks for one', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    const { service } = createService();

    service.markPasswordChangeRequired();

    expect(service.passwordChangeRequired()).toBe(true);
    expect(localStorage.getItem(PASSWORD_CHANGE_STORAGE_KEY)).toBe('true');
  });

  it('clears everything on logout', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    localStorage.setItem(PASSWORD_CHANGE_STORAGE_KEY, 'true');
    const { service } = createService();

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
    expect(service.passwordChangeRequired()).toBe(false);
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
    expect(localStorage.getItem(PASSWORD_CHANGE_STORAGE_KEY)).toBeNull();
  });
});
