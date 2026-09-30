import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeObjectUrls } from '../../../testing/object-urls';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { ResourcePhotoService } from './resource-photo.service';

describe('ResourcePhotoService', () => {
  let service: ResourcePhotoService;
  let http: HttpTestingController;
  let urls: ReturnType<typeof fakeObjectUrls>;
  const photoUrl = (id: string) => `${environment.apiUrl}/resources/${id}/photo`;

  beforeEach(() => {
    urls = fakeObjectUrls();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ResourcePhotoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    urls.restore();
  });

  it('fetches the photo as a blob, marked with its version, and without error toasts', () => {
    let url: string | null | undefined;
    service.photoUrl('res/1', '2026-10-05T08:00:00Z').subscribe((u) => (url = u));

    const req = http.expectOne((r) => r.url === photoUrl('res%2F1'));
    expect(req.request.params.get('v')).toBe('2026-10-05T08:00:00Z');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(new Blob(['png'], { type: 'image/png' }));

    expect(url).toBe('blob:test-1');
  });

  it('fetches each version once, however many places show it', () => {
    const seen: (string | null)[] = [];
    service.photoUrl('res-1', 'v1').subscribe((u) => seen.push(u));
    http.expectOne((r) => r.url === photoUrl('res-1')).flush(new Blob(['png']));

    service.photoUrl('res-1', 'v1').subscribe((u) => seen.push(u));

    http.expectNone((r) => r.url === photoUrl('res-1'));
    expect(seen).toEqual(['blob:test-1', 'blob:test-1']);
  });

  it('fetches a new version and lets go of the old one', () => {
    service.photoUrl('res-1', 'v1').subscribe();
    http.expectOne((r) => r.params.get('v') === 'v1').flush(new Blob(['old']));

    let url: string | null | undefined;
    service.photoUrl('res-1', 'v2').subscribe((u) => (url = u));
    http.expectOne((r) => r.params.get('v') === 'v2').flush(new Blob(['new']));

    expect(url).toBe('blob:test-2');
    expect(urls.revoked).toEqual(['blob:test-1']);
  });

  it('answers null when the photo fails to load, and tries again next time', () => {
    let url: string | null | undefined;
    service.photoUrl('res-1', 'v1').subscribe((u) => (url = u));
    http
      .expectOne((r) => r.url === photoUrl('res-1'))
      .flush(null, { status: 404, statusText: 'Not Found' });
    expect(url).toBeNull();

    service.photoUrl('res-1', 'v1').subscribe((u) => (url = u));
    http.expectOne((r) => r.url === photoUrl('res-1')).flush(new Blob(['png']));
    expect(url).toBe('blob:test-1');
  });

  it('gives each type its picture, and other types a neutral one', () => {
    expect(service.typeImage('Meeting Room')).toBe('images/resource-types/meeting-room.webp');
    expect(service.typeImage('Desk')).toBe('images/resource-types/desk.webp');
    expect(service.typeImage('Company Car')).toBe('images/resource-types/company-car.webp');
    expect(service.typeImage('Projector')).toBe('images/resource-types/projector.webp');
    expect(service.typeImage('Camera')).toBe('images/resource-types/camera.webp');
    expect(service.typeImage('Parking Space')).toBe('images/resource-types/other.svg');
  });
});
