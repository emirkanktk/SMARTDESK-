import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { languageInterceptor } from './language.interceptor';

describe('languageInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  function setup(locale: string): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([languageInterceptor])),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: locale },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  }

  afterEach(() => backend.verify());

  it("asks the API for the app's language", () => {
    setup('tr');
    http.get(`${environment.apiUrl}/resources`).subscribe();

    const req = backend.expectOne(`${environment.apiUrl}/resources`);
    expect(req.request.headers.get('Accept-Language')).toBe('tr');
    req.flush([]);
  });

  it('leaves requests to other hosts alone', () => {
    setup('tr');
    http.get('https://example.com/data.json').subscribe();

    const req = backend.expectOne('https://example.com/data.json');
    expect(req.request.headers.has('Accept-Language')).toBe(false);
    req.flush({});
  });
});
