import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ReportService } from './report.service';

describe('ReportService', () => {
  let service: ReportService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/reports/usage`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ReportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the usage report of a range in a time zone', () => {
    service
      .getUsage({ from: '2026-09-01', to: '2026-09-30', timeZone: 'Europe/Istanbul' })
      .subscribe();

    const req = http.expectOne((r) => r.url === url);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-09-01');
    expect(req.request.params.get('to')).toBe('2026-09-30');
    expect(req.request.params.get('timeZone')).toBe('Europe/Istanbul');
    expect(req.request.params.has('resourceTypeId')).toBe(false);
    req.flush({});
  });

  it('adds the type filter only when one is chosen', () => {
    service
      .getUsage({
        from: '2026-09-01',
        to: '2026-09-30',
        timeZone: 'UTC',
        resourceTypeId: 'type-desk',
      })
      .subscribe();

    const req = http.expectOne((r) => r.url === url);
    expect(req.request.params.get('resourceTypeId')).toBe('type-desk');
    req.flush({});
  });

  it('fetches the same report as a workbook, as a file', () => {
    service
      .getUsageWorkbook({
        from: '2026-09-01',
        to: '2026-09-30',
        timeZone: 'Europe/Istanbul',
        resourceTypeId: 'type-desk',
      })
      .subscribe();

    const req = http.expectOne((r) => r.url === `${url}/workbook`);
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('from')).toBe('2026-09-01');
    expect(req.request.params.get('timeZone')).toBe('Europe/Istanbul');
    expect(req.request.params.get('resourceTypeId')).toBe('type-desk');
    req.flush(new Blob(['xlsx']));
  });
});
