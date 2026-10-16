import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { ApprovalService } from './approval.service';

describe('ApprovalService', () => {
  const base = `${environment.apiUrl}/approvals`;
  let service: ApprovalService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ApprovalService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads what waits, raising a toast on failure unless asked not to', () => {
    service.getWaiting().subscribe();
    service.getWaiting(true).subscribe();

    const [loud, quiet] = http.match(base);
    expect(loud.request.method).toBe('GET');
    expect(loud.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(false);
    expect(quiet.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
  });

  it('approves, and declines with the reason', () => {
    service.approve('res-1').subscribe();
    service.decline('res-2', 'The car is at the service').subscribe();

    const approve = http.expectOne(`${base}/res-1/approve`);
    expect(approve.request.method).toBe('POST');
    const decline = http.expectOne(`${base}/res-2/decline`);
    expect(decline.request.method).toBe('POST');
    expect(decline.request.body).toEqual({ reason: 'The car is at the service' });
  });
});
