import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { ReservationService } from './reservation.service';

describe('ReservationService', () => {
  let service: ReservationService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/reservations`;
  const body = {
    resourceId: 'res-1',
    startDateTime: '2026-10-07T11:00:00.000Z',
    endDateTime: '2026-10-07T12:30:00.000Z',
    note: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ReservationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it("gets the caller's reservations", () => {
    service.getMine().subscribe();

    const req = http.expectOne(`${base}/mine`);
    expect(req.request.method).toBe('GET');
    req.flush([fakeReservation()]);
  });

  it('cancels with POST to the cancel action, without a body', () => {
    service.cancel('rsv-1').subscribe();

    const req = http.expectOne(`${base}/rsv-1/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(fakeReservation({ status: 'Cancelled' }));
  });

  it('gets one reservation by id', () => {
    service.getReservation('rsv-1').subscribe();

    const req = http.expectOne(`${base}/rsv-1`);
    expect(req.request.method).toBe('GET');
    req.flush(fakeReservation());
  });

  it('creates with POST', () => {
    service.create(body).subscribe();

    const req = http.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(fakeReservation());
  });

  it('updates with PUT to the reservation', () => {
    service.update('rsv-1', body).subscribe();

    const req = http.expectOne(`${base}/rsv-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(fakeReservation());
  });
});
