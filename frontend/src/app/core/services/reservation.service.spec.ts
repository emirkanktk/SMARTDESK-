import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { Recurrence } from '../models/reservation.model';
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

  it('ends a booking under way with POST to end, without a body', () => {
    service.end('rsv-1').subscribe();

    const req = http.expectOne(`${base}/rsv-1/end`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(fakeReservation());
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

  describe('bookings that repeat (ADR-0035)', () => {
    const recurrence: Recurrence = {
      frequency: 'Weekly',
      interval: 2,
      daysOfWeek: ['Monday', 'Thursday'],
      until: '2026-11-30',
      timeZone: 'Europe/Istanbul',
    };

    it('asks for the dates of a series with the rule in the query, each day on its own, quietly', () => {
      service
        .previewSeries({
          resourceId: 'res-1',
          startDateTime: body.startDateTime,
          endDateTime: body.endDateTime,
          recurrence,
        })
        .subscribe();

      const req = http.expectOne((r) => r.url === `${base}/series/preview`);
      expect(req.request.method).toBe('GET');
      const params = req.request.params;
      expect(params.get('resourceId')).toBe('res-1');
      expect(params.get('startDateTime')).toBe(body.startDateTime);
      expect(params.get('endDateTime')).toBe(body.endDateTime);
      expect(params.get('frequency')).toBe('Weekly');
      expect(params.get('interval')).toBe('2');
      expect(params.getAll('daysOfWeek')).toEqual(['Monday', 'Thursday']);
      expect(params.get('until')).toBe('2026-11-30');
      expect(params.get('timeZone')).toBe('Europe/Istanbul');
      expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
      req.flush({ dates: [] });
    });

    it('books a series with POST', () => {
      const request = { ...body, recurrence, skipConflicts: true };
      service.createSeries(request).subscribe();

      const req = http.expectOne(`${base}/series`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ series: { id: 's-1', ...recurrence }, booked: [], skipped: [] });
    });

    it('cancels a date and the later ones with POST to cancel-following, without a body', () => {
      let cancelled: string[] = [];
      service.cancelFollowing('rsv-2').subscribe((dates) => (cancelled = dates.map((d) => d.id)));

      const req = http.expectOne(`${base}/rsv-2/cancel-following`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toBeNull();
      req.flush([fakeReservation({ id: 'rsv-2' }), fakeReservation({ id: 'rsv-3' })]);
      expect(cancelled).toEqual(['rsv-2', 'rsv-3']);
    });
  });
});
