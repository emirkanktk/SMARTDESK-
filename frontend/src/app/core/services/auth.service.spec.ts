import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { expiredJwt, fakeJwt } from '../../../testing/fake-jwt';
import { Roles } from '../constants/roles';
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

  it('clears everything on logout', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    const { service } = createService();

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getToken()).toBeNull();
    expect(localStorage.getItem(TOKEN_STORAGE_KEY)).toBeNull();
  });
});
