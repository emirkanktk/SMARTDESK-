import { ApplicationRef, Component } from '@angular/core';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ApiError } from '../models/api-error.model';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { errorInterceptor } from './error.interceptor';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from './http-context.tokens';

@Component({ template: '' })
class Blank {}

describe('errorInterceptor', () => {
  const url = `${environment.apiUrl}/thing`;
  let http: HttpClient;
  let backend: HttpTestingController;
  let notifications: NotificationService;
  let router: Router;
  let logout: ReturnType<typeof vi.fn>;
  let markPasswordChangeRequired: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    logout = vi.fn();
    markPasswordChangeRequired = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: Blank },
          { path: '**', component: Blank },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { logout, markPasswordChangeRequired } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    notifications = TestBed.inject(NotificationService);
    router = TestBed.inject(Router);
  });

  afterEach(() => backend.verify());

  function request(options: { url?: string; context?: HttpContext } = {}): {
    error: () => unknown;
  } {
    let error: unknown;
    http.get(options.url ?? url, { context: options.context }).subscribe({
      error: (e: unknown) => (error = e),
    });
    return { error: () => error };
  }

  function flush401(target = url): void {
    backend.expectOne(target).flush({}, { status: 401, statusText: 'Unauthorized' });
  }

  it('maps a validation problem to an ApiError with field errors, without a toast', () => {
    const result = request();
    backend
      .expectOne(url)
      .flush(
        { title: 'One or more validation errors occurred.', errors: { Email: ['Required.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    const error = result.error() as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.fieldErrors).toEqual({ Email: ['Required.'] });
    expect(notifications.notifications()).toEqual([]);
  });

  it('ends the session on a 401 from the API, even when no token was sent', async () => {
    await router.navigateByUrl('/resources');
    const navigate = vi.spyOn(router, 'navigate');

    request();
    flush401();

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/resources' },
    });
    expect(notifications.notifications()[0]?.message).toContain('session has expired');
  });

  it('does not end the session on a 401 for an anonymous request (failed login)', () => {
    const navigate = vi.spyOn(router, 'navigate');

    const result = request({ context: new HttpContext().set(ANONYMOUS_REQUEST, true) });
    flush401();

    expect(logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect((result.error() as ApiError).status).toBe(401);
  });

  it('does not end the session on a 401 from another origin', () => {
    request({ url: 'https://example.com/data' });
    flush401('https://example.com/data');

    expect(logout).not.toHaveBeenCalled();
  });

  it('handles several 401s arriving together with one redirect and one toast', async () => {
    await router.navigateByUrl('/dashboard');
    const navigate = vi.spyOn(router, 'navigate');

    request();
    request();
    request();
    backend.match(url).forEach((req) => req.flush({}, { status: 401, statusText: 'Unauthorized' }));
    await TestBed.inject(ApplicationRef).whenStable();

    expect(navigate).toHaveBeenCalledTimes(1);
    expect(router.url).toBe('/login?returnUrl=%2Fdashboard');
    expect(notifications.notifications().length).toBe(1);
  });

  it('does not redirect again when already on the login screen', async () => {
    await router.navigateByUrl('/login');
    const navigate = vi.spyOn(router, 'navigate');

    request();
    flush401();

    expect(navigate).not.toHaveBeenCalled();
  });

  it('sends a user whose password must be changed to change it, without a toast', async () => {
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl');

    const result = request();
    backend.expectOne(url).flush(
      {
        title: 'Password change required',
        detail: 'Choose a new password before you continue.',
        code: 'password-change-required',
      },
      { status: 403, statusText: 'Forbidden' },
    );

    expect((result.error() as ApiError).code).toBe('password-change-required');
    expect(markPasswordChangeRequired).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/change-password');
    expect(logout).not.toHaveBeenCalled();
    expect(notifications.notifications()).toEqual([]);
  });

  it('still says "not allowed" for any other 403', () => {
    request();
    backend.expectOne(url).flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });

    expect(markPasswordChangeRequired).not.toHaveBeenCalled();
    expect(notifications.notifications()[0]?.message).toBe(
      'You do not have permission to do that.',
    );
  });

  it('shows a toast for server errors', () => {
    request();
    backend.expectOne(url).flush({}, { status: 500, statusText: 'Server Error' });

    expect(notifications.notifications()[0]?.kind).toBe('error');
  });

  it('shows no toast when the caller opted out', () => {
    request({ context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true) });
    backend.expectOne(url).flush({}, { status: 500, statusText: 'Server Error' });

    expect(notifications.notifications()).toEqual([]);
  });

  it('reports an unreachable server as status 0', () => {
    const result = request();
    backend.expectOne(url).error(new ProgressEvent('error'));

    expect((result.error() as ApiError).status).toBe(0);
    expect(notifications.notifications()[0]?.message).toContain('Cannot reach the server');
  });
});
