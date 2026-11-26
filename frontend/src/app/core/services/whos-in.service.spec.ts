import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { browserTimeZone } from './dashboard.service';
import { WhosInService } from './whos-in.service';

describe('WhosInService', () => {
  let service: WhosInService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WhosInService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it("asks for a day as the browser's clock has it, and leaves failures to the page", () => {
    service.get('2026-11-27').subscribe();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/whos-in`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('date')).toBe('2026-11-27');
    expect(req.request.params.get('timeZone')).toBe(browserTimeZone());
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush({ date: '2026-11-27', timeZone: browserTimeZone(), hasTeam: false, people: [] });
  });
});
