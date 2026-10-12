import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { FloorPlanService } from './floor-plan.service';

describe('FloorPlanService', () => {
  const base = `${environment.apiUrl}/floor-plans`;
  let service: FloorPlanService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FloorPlanService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the floors', () => {
    service.getFloors().subscribe();

    expect(http.expectOne(base).request.method).toBe('GET');
  });

  it('fetches a plan by its floor and version, as a picture, without a toast', () => {
    service.getPlan('5th Floor', '2026-10-12T08:00:00Z').subscribe();

    const req = http.expectOne((r) => r.url === `${base}/image`);
    expect(req.request.params.get('location')).toBe('5th Floor');
    expect(req.request.params.get('v')).toBe('2026-10-12T08:00:00Z');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
  });

  it('uploads a plan as the multipart field "plan" for its floor, and the page explains a refusal', () => {
    const plan = new Blob(['webp'], { type: 'image/webp' });
    service.setPlan('5th Floor', plan).subscribe();

    const req = http.expectOne((r) => r.url === `${base}/image`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.params.get('location')).toBe('5th Floor');
    const file = (req.request.body as FormData).get('plan') as File;
    expect(file.name).toBe('plan.webp');
    expect(file.type).toBe('image/webp');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
  });

  it('removes a plan', () => {
    service.removePlan('5th Floor').subscribe();

    const req = http.expectOne((r) => r.url === `${base}/image`);
    expect(req.request.method).toBe('DELETE');
    expect(req.request.params.get('location')).toBe('5th Floor');
  });
});
