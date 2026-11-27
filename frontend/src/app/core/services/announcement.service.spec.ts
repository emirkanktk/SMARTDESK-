import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AnnouncementService } from './announcement.service';

describe('AnnouncementService', () => {
  const url = `${environment.apiUrl}/announcements`;
  let service: AnnouncementService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AnnouncementService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the ones showing now quietly, as the dashboard works without them', () => {
    service.current().subscribe();

    const req = http.expectOne(url);
    expect(req.request.method).toBe('GET');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush([]);
  });

  it('lists them all, posts one and takes one down', () => {
    service.all().subscribe();
    expect(http.expectOne(`${url}/all`).request.method).toBe('GET');

    const posted = {
      title: 'Breakfast on Friday',
      body: null,
      tone: 'Good' as const,
      endsAt: '2026-12-04T21:00:00.000Z',
      notify: true,
    };
    service.create(posted).subscribe();
    const create = http.expectOne(url);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual(posted);
    expect(create.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);

    service.remove('ann-1').subscribe();
    expect(http.expectOne(`${url}/ann-1`).request.method).toBe('DELETE');
  });
});
