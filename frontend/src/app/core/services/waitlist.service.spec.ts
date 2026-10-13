import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { WaitlistService } from './waitlist.service';

describe('WaitlistService', () => {
  const base = `${environment.apiUrl}/waitlist`;
  let service: WaitlistService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WaitlistService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists what the caller waits for', () => {
    service.getMine().subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
  });

  it('joins with the resource and the time, and leaves by id', () => {
    const request = {
      resourceId: 'res-1',
      startDateTime: '2026-10-07T12:00:00.000Z',
      endDateTime: '2026-10-07T13:00:00.000Z',
    };
    service.join(request).subscribe();
    service.leave('wait/1').subscribe();

    const join = http.expectOne({ method: 'POST', url: base });
    expect(join.request.body).toEqual(request);
    http.expectOne({ method: 'DELETE', url: `${base}/wait%2F1` });
  });
});
