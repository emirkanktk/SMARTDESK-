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
    reminderMinutes: 15,
    attendeeIds: ['u-mehmet'],
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

  it('gets the meetings the caller is invited to', () => {
    service.getInvited().subscribe();

    const req = http.expectOne(`${base}/invited`);
    expect(req.request.method).toBe('GET');
    req.flush([fakeReservation()]);
  });

  it('gets who the caller may invite', () => {
    let names: string[] = [];
    service.getInvitees().subscribe((people) => (names = people.map((p) => p.name)));

    const req = http.expectOne(`${base}/invitees`);
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'u-mehmet', name: 'Mehmet Demir', email: 'mehmet@company.example' }]);
    expect(names).toEqual(['Mehmet Demir']);
  });

  it('asks how much longer a booking can run', () => {
    let minutes: number[] = [];
    service.getExtensionOptions('rsv-1').subscribe((options) => (minutes = options.minutes));

    const req = http.expectOne(`${base}/rsv-1/extension`);
    expect(req.request.method).toBe('GET');
    req.flush({ minutes: [15, 30] });
    expect(minutes).toEqual([15, 30]);
  });

  it('extends with POST to extend, saying by how much', () => {
    service.extend('rsv-1', 30).subscribe();

    const req = http.expectOne(`${base}/rsv-1/extend`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ minutes: 30 });
    req.flush(fakeReservation());
  });

  it('answers an invitation with POST to its response', () => {
    service.respond('rsv-1', { response: 'Declined', reason: 'Off sick' }).subscribe();

    const req = http.expectOne(`${base}/rsv-1/response`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ response: 'Declined', reason: 'Off sick' });
    req.flush(fakeReservation());
  });

  it('cancels with POST to the cancel action, without a body', () => {
    service.cancel('rsv-1').subscribe();

    const req = http.expectOne(`${base}/rsv-1/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(fakeReservation({ status: 'Cancelled' }));
  });

  it('asks the calendar for a range, with only the filters that are set', () => {
    service
      .getCalendar({
        from: '2026-10-04T21:00:00.000Z',
        to: '2026-10-11T21:00:00.000Z',
        resourceTypeId: 'type-room',
        resourceId: null,
      })
      .subscribe();

    const req = http.expectOne((r) => r.url === base);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('from')).toBe('2026-10-04T21:00:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-10-11T21:00:00.000Z');
    expect(req.request.params.get('resourceTypeId')).toBe('type-room');
    expect(req.request.params.has('resourceId')).toBe(false);
    req.flush([]);
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

  it('updates with PUT to the reservation, the list of who is invited included', () => {
    service.update('rsv-1', body).subscribe();

    const req = http.expectOne(`${base}/rsv-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(fakeReservation());
  });
});
