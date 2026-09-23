import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeResource } from '../../../testing/resource-fixtures';
import { ResourceService } from './resource.service';

describe('ResourceService', () => {
  let service: ResourceService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/resources`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ResourceService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filters that are set', () => {
    service.getResources({ resourceTypeId: 'type-room', status: null }).subscribe();

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.get('resourceTypeId')).toBe('type-room');
    expect(req.request.params.has('status')).toBe(false);
    req.flush([]);
  });

  it('sends no query string without filters', () => {
    service.getResources().subscribe();

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.params.keys()).toEqual([]);
    req.flush([]);
  });

  it('creates with POST and updates with PUT on the resource url', () => {
    const { id, resourceTypeName: _, status, ...body } = fakeResource();

    service.create(body).subscribe();
    const create = http.expectOne({ method: 'POST', url: base });
    expect(create.request.body).toEqual(body);
    create.flush(fakeResource());

    service.update(id, { ...body, status }).subscribe();
    const update = http.expectOne({ method: 'PUT', url: `${base}/${id}` });
    expect(update.request.body.status).toBe('Active');
    update.flush(fakeResource());
  });
});
