import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { BookingRulesService } from './booking-rules.service';

describe('BookingRulesService', () => {
  const base = `${environment.apiUrl}/booking-rules`;
  let service: BookingRulesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BookingRulesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the rules, raising a toast on failure unless asked not to', () => {
    service.get().subscribe();
    service.get(true).subscribe();

    const [loud, quiet] = http.match(base);
    expect(loud.request.method).toBe('GET');
    expect(loud.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(false);
    expect(quiet.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
  });

  it("sets the office's hours, and a kind's rules by its id", () => {
    const hours = {
      timeZone: 'Europe/Istanbul',
      opensAt: '08:30',
      closesAt: '17:00',
      workdays: ['Monday' as const],
    };
    const rules = {
      maxDurationMinutes: 540,
      maxDaysAhead: 7,
      maxActivePerUser: null,
      officeHoursOnly: true,
    };
    service.setOfficeHours(hours).subscribe();
    service.setTypeRules('type/1', rules).subscribe();

    expect(http.expectOne({ method: 'PUT', url: `${base}/office-hours` }).request.body).toEqual(
      hours,
    );
    expect(http.expectOne({ method: 'PUT', url: `${base}/types/type%2F1` }).request.body).toEqual(
      rules,
    );
  });
});
