import {
  HttpClient,
  HttpContext,
  HttpHeaders,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiError } from '../models/api-error.model';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { errorInterceptor } from './error.interceptor';
import { SKIP_ERROR_NOTIFICATION } from './http-context.tokens';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let notifications: NotificationService;
  let logout: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    logout = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { logout } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    notifications = TestBed.inject(NotificationService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => backend.verify());

  function request(options: { authorized?: boolean; context?: HttpContext } = {}): {
    error: () => unknown;
  } {
    let error: unknown;
    const headers = options.authorized ? new HttpHeaders({ Authorization: 'Bearer t' }) : undefined;
    http.get('/api/thing', { headers, context: options.context }).subscribe({
      error: (e: unknown) => (error = e),
    });
    return { error: () => error };
  }

  it('maps a validation problem to an ApiError with field errors, without a toast', () => {
    const result = request();
    backend
      .expectOne('/api/thing')
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

  it('ends the session on a 401 for a request that carried a token', () => {
    request({ authorized: true });
    backend.expectOne('/api/thing').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
  });

  it('does not end the session on a 401 without a token (failed login)', () => {
    const result = request();
    backend.expectOne('/api/thing').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(logout).not.toHaveBeenCalled();
    expect((result.error() as ApiError).status).toBe(401);
  });

  it('shows a toast for server errors', () => {
    request();
    backend.expectOne('/api/thing').flush({}, { status: 500, statusText: 'Server Error' });

    expect(notifications.notifications()[0]?.kind).toBe('error');
  });

  it('shows no toast when the caller opted out', () => {
    request({ context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true) });
    backend.expectOne('/api/thing').flush({}, { status: 500, statusText: 'Server Error' });

    expect(notifications.notifications()).toEqual([]);
  });

  it('reports an unreachable server as status 0', () => {
    const result = request();
    backend.expectOne('/api/thing').error(new ProgressEvent('error'));

    expect((result.error() as ApiError).status).toBe(0);
    expect(notifications.notifications()[0]?.message).toContain('Cannot reach the server');
  });
});
