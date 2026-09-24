import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DashboardService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for today in the given time zone', () => {
    service.getDashboard('Europe/Istanbul').subscribe();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/dashboard`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('timeZone')).toBe('Europe/Istanbul');
    req.flush({});
  });
});
