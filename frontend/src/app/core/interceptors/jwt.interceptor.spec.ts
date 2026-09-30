import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { ANONYMOUS_REQUEST } from './http-context.tokens';
import { jwtInterceptor } from './jwt.interceptor';

describe('jwtInterceptor', () => {
  let token: Observable<string | null>;
  let validToken: ReturnType<typeof vi.fn>;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    token = of('the-token');
    validToken = vi.fn(() => token);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { validToken } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API requests', () => {
    http.get(`${environment.apiUrl}/resources`).subscribe();

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer the-token');
    req.flush([]);
  });

  it('renews a token about to expire before the request goes out', () => {
    const renewed = new Subject<string | null>();
    token = renewed;

    http.get(`${environment.apiUrl}/resources`).subscribe();
    backend.expectNone(`${environment.apiUrl}/resources`);
    renewed.next('renewed-token');
    renewed.complete();

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer renewed-token');
    req.flush([]);
  });

  it('sends API requests without a header when signed out', () => {
    token = of(null);
    http.get(`${environment.apiUrl}/resources`).subscribe();

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('sends anonymous requests (login) without a token', () => {
    http
      .post(
        `${environment.apiUrl}/auth/login`,
        {},
        {
          context: new HttpContext().set(ANONYMOUS_REQUEST, true),
        },
      )
      .subscribe();

    const req = backend.expectOne(`${environment.apiUrl}/auth/login`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(validToken).not.toHaveBeenCalled();
    req.flush({});
  });

  it('never sends the token to another origin', () => {
    http.get('https://example.com/data').subscribe();

    const req = backend.expectOne('https://example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(validToken).not.toHaveBeenCalled();
    req.flush({});
  });
});
