import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeIssue } from '../../../testing/issue-fixtures';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { IssueService } from './issue.service';

describe('IssueService', () => {
  let service: IssueService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(IssueService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it("asks for a resource's open problems on the resource", () => {
    service.getOpen('res 1').subscribe();

    http.expectOne(`${environment.apiUrl}/resources/res%201/issues`).flush([]);
  });

  it('reports a problem as a form, with the photo when there is one, and no toast on failure', () => {
    const photo = new Blob(['webp'], { type: 'image/webp' });
    let created: unknown;
    service
      .report('res-1', { category: 'Display', description: 'Dead TV', isBlocking: true, photo })
      .subscribe((issue) => (created = issue));

    const req = http.expectOne(`${environment.apiUrl}/resources/res-1/issues`);
    expect(req.request.method).toBe('POST');
    const form = req.request.body as FormData;
    expect(form.get('category')).toBe('Display');
    expect(form.get('description')).toBe('Dead TV');
    expect(form.get('isBlocking')).toBe('true');
    expect((form.get('photo') as File).name).toBe('photo.webp');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(fakeIssue());
    expect(created).toEqual(fakeIssue());
  });

  it('sends no photo field without a photo', () => {
    service
      .report('res-1', {
        category: 'Other',
        description: 'Loose handle',
        isBlocking: false,
        photo: null,
      })
      .subscribe();

    const req = http.expectOne(`${environment.apiUrl}/resources/res-1/issues`);
    expect((req.request.body as FormData).has('photo')).toBe(false);
    expect((req.request.body as FormData).get('isBlocking')).toBe('false');
    req.flush(fakeIssue());
  });

  it('lists the reports with only the filters that are set', () => {
    service.getAll({ status: 'Open', resourceId: null }).subscribe();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/issues`);
    expect(req.request.params.get('status')).toBe('Open');
    expect(req.request.params.has('resourceId')).toBe(false);
    req.flush([]);
  });

  it('moves a report on with PATCH, and fetches its photo as a blob', () => {
    service.updateStatus('iss-1', 'Resolved').subscribe();
    const patch = http.expectOne(`${environment.apiUrl}/issues/iss-1`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ status: 'Resolved' });
    patch.flush(fakeIssue({ status: 'Resolved' }));

    service.getPhoto('iss-1').subscribe();
    const photo = http.expectOne(`${environment.apiUrl}/issues/iss-1/photo`);
    expect(photo.request.responseType).toBe('blob');
    photo.flush(new Blob(['png']));
  });
});
