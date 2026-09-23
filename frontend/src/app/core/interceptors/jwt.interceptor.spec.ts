import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { jwtInterceptor } from './jwt.interceptor';

describe('jwtInterceptor', () => {
  let token: string | null;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    token = 'the-token';
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { getToken: () => token } },
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

  it('sends API requests without a header when signed out', () => {
    token = null;
    http.get(`${environment.apiUrl}/resources`).subscribe();

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('never sends the token to another origin', () => {
    http.get('https://example.com/data').subscribe();

    const req = backend.expectOne('https://example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
