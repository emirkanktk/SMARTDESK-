import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { CalendarService } from './calendar.service';

describe('CalendarService', () => {
  const subscription = `${environment.apiUrl}/calendar/subscription`;
  let service: CalendarService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CalendarService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the link quietly, as nothing when there is none (204)', () => {
    let read: unknown = 'not yet';
    service.getSubscription().subscribe((value) => (read = value));

    const request = http.expectOne(subscription);
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(read).toBeNull();
  });

  it('makes a link and removes it', () => {
    const made = {
      url: `http://localhost:4200/api/calendar/feed.ics?token=${'a'.repeat(64)}`,
      createdAt: '2026-10-15T08:00:00Z',
      lastFetchedAt: null,
    };
    let answer: unknown;
    service.createSubscription().subscribe((value) => (answer = value));
    service.deleteSubscription().subscribe();

    const [create, remove] = http.match(subscription);
    expect(create.request.method).toBe('POST');
    expect(remove.request.method).toBe('DELETE');
    create.flush(made);
    remove.flush(null, { status: 204, statusText: 'No Content' });
    expect(answer).toEqual(made);
  });

  it('fetches a booking as an .ics file', () => {
    let file: Blob | undefined;
    service.getReservationFile('res-1').subscribe((value) => (file = value));

    const request = http.expectOne(`${environment.apiUrl}/reservations/res-1/calendar`);
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['BEGIN:VCALENDAR'], { type: 'text/calendar' }));
    expect(file?.type).toBe('text/calendar');
  });
});
