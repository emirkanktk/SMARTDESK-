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

  it('searches availability with the time range and only the filters that are set', () => {
    service
      .getAvailable({
        startDateTime: '2026-10-05T07:00:00.000Z',
        endDateTime: '2026-10-05T08:00:00.000Z',
        resourceTypeId: null,
        minCapacity: 4,
      })
      .subscribe();

    const req = http.expectOne((r) => r.url === `${base}/available`);
    expect(req.request.params.get('startDateTime')).toBe('2026-10-05T07:00:00.000Z');
    expect(req.request.params.get('endDateTime')).toBe('2026-10-05T08:00:00.000Z');
    expect(req.request.params.get('minCapacity')).toBe('4');
    expect(req.request.params.has('resourceTypeId')).toBe(false);
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

  it('sets the teams with PATCH, an empty list meaning shared', () => {
    service.setTeams('res/1', ['u-zeynep']).subscribe();
    const give = http.expectOne({ method: 'PATCH', url: `${base}/res%2F1/teams` });
    expect(give.request.body).toEqual({ teamIds: ['u-zeynep'] });
    give.flush(fakeResource());

    service.setTeams('res-1', []).subscribe();
    const share = http.expectOne({ method: 'PATCH', url: `${base}/res-1/teams` });
    expect(share.request.body).toEqual({ teamIds: [] });
    share.flush(fakeResource());
  });

  it('changes only the status with PATCH', () => {
    service.changeStatus('res/1', 'Maintenance').subscribe();

    const req = http.expectOne({ method: 'PATCH', url: `${base}/res%2F1/status` });
    expect(req.request.body).toEqual({ status: 'Maintenance' });
    req.flush(fakeResource({ status: 'Maintenance' }));
  });
});
