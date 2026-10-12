import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeResource } from '../../../testing/resource-fixtures';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
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

  it('lists the kinds of resources in the order their names read', () => {
    let names: string[] = [];
    service.getResourceTypes().subscribe((types) => (names = types.map((t) => t.name)));

    http.expectOne(`${environment.apiUrl}/resource-types`).flush([
      { id: 'p', name: 'Projector' },
      { id: 'c', name: 'Camera' },
      { id: 'm', name: 'Meeting Room' },
    ]);

    expect(names).toEqual(['Camera', 'Meeting Room', 'Projector']);
  });

  it('places a resource on its floor plan, or takes it off with no place', () => {
    service.setPosition('res 1', { x: 0.25, y: 0.5 }).subscribe();
    service.setPosition('res 1', null).subscribe();

    const [placed, taken] = http.match(`${base}/res%201/position`);
    expect(placed.request.method).toBe('PUT');
    expect(placed.request.body).toEqual({ x: 0.25, y: 0.5 });
    expect(taken.request.body).toEqual({ x: null, y: null });
  });

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

  it('puts a photo up as the multipart field "photo", named after its type', () => {
    const photo = new Blob(['webp'], { type: 'image/webp' });
    service.setPhoto('res/1', photo).subscribe();

    const req = http.expectOne({ method: 'PUT', url: `${base}/res%2F1/photo` });
    const body = req.request.body as FormData;
    const file = body.get('photo') as File;
    expect(file.name).toBe('photo.webp');
    expect(file.type).toBe('image/webp');
    req.flush(fakeResource({ photoUpdatedAt: '2026-10-05T08:00:00Z' }));
  });

  it('takes a photo down with DELETE', () => {
    service.removePhoto('res/1').subscribe();

    http.expectOne({ method: 'DELETE', url: `${base}/res%2F1/photo` }).flush(null);
  });

  it('changes only the status with PATCH', () => {
    service.changeStatus('res/1', 'Maintenance').subscribe();

    const req = http.expectOne({ method: 'PATCH', url: `${base}/res%2F1/status` });
    expect(req.request.body).toEqual({ status: 'Maintenance' });
    req.flush(fakeResource({ status: 'Maintenance' }));
  });

  it('gets the QR code with GET and replaces it with POST', () => {
    const code = { code: 'c0de', url: 'http://localhost:4200/check-in/res-1?code=c0de' };
    service.getCheckInCode('res/1').subscribe();
    service.replaceCheckInCode('res/1').subscribe();

    http.expectOne({ method: 'GET', url: `${base}/res%2F1/check-in-code` }).flush(code);
    const replace = http.expectOne({ method: 'POST', url: `${base}/res%2F1/check-in-code` });
    expect(replace.request.body).toBeNull();
    replace.flush(code);
  });

  it('sets up a door screen with POST and turns it off with DELETE', () => {
    let url = '';
    service.setUpDoorScreen('res/1').subscribe((link) => (url = link.url));
    service.turnOffDoorScreen('res/1').subscribe();

    const setUp = http.expectOne({ method: 'POST', url: `${base}/res%2F1/door-screen` });
    expect(setUp.request.body).toBeNull();
    setUp.flush({ url: 'http://localhost:4200/door/res-1#t0k3n', setUpAt: '2026-10-01T09:00:00Z' });
    http
      .expectOne({ method: 'DELETE', url: `${base}/res%2F1/door-screen` })
      .flush(null, { status: 204, statusText: 'No Content' });
    expect(url).toBe('http://localhost:4200/door/res-1#t0k3n');
  });

  it('checks in with the code, and leaves explaining a refusal to the page', () => {
    service.checkIn('res/1', 'c0de').subscribe();

    const req = http.expectOne({ method: 'POST', url: `${base}/res%2F1/check-in` });
    expect(req.request.body).toEqual({ code: 'c0de' });
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(null);
  });
});
