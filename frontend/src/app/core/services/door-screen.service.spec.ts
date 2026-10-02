import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { DoorScreenService } from './door-screen.service';

describe('DoorScreenService', () => {
  it('asks with the screen token in a header, no user, and its own time zone', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([])), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(DoorScreenService).get('res/1', 't0k3n', 'Europe/Istanbul').subscribe();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/door/res%2F1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.get('X-Door-Token')).toBe('t0k3n');
    expect(req.request.params.get('timeZone')).toBe('Europe/Istanbul');
    expect(req.request.urlWithParams).not.toContain('t0k3n');
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush({});
    http.verify();
  });
});
