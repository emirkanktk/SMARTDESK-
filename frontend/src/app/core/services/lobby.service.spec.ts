import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { LOBBY_TOKEN_HEADER } from '../models/lobby.model';
import { LobbyService } from './lobby.service';

describe('LobbyService', () => {
  let service: LobbyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LobbyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks as the screen, with its token in a header and no user', () => {
    service.get('c'.repeat(64), 'Europe/Istanbul').subscribe();

    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/lobby`);
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.get(LOBBY_TOKEN_HEADER)).toBe('c'.repeat(64));
    expect(req.request.params.get('timeZone')).toBe('Europe/Istanbul');
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush({});
  });

  it('lists, sets up and takes down the screens', () => {
    const url = `${environment.apiUrl}/lobby-screens`;

    service.list().subscribe();
    expect(http.expectOne(url).request.method).toBe('GET');

    service.create('Entrance', null).subscribe();
    const create = http.expectOne(url);
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ name: 'Entrance', location: null });
    expect(create.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);

    service.remove('screen-1').subscribe();
    expect(http.expectOne(`${url}/screen-1`).request.method).toBe('DELETE');
  });
});
