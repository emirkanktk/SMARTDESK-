import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import {
  CHECK_IN,
  DEFAULT_CHECK_IN_TIMES,
  checkInWindow,
  setCheckInTimes,
} from '../models/reservation.model';
import { CHECK_IN_TIMES_WAIT_MS, CheckInTimesService } from './check-in-times.service';

describe('CheckInTimesService', () => {
  const url = `${environment.apiUrl}/check-in/times`;
  const oneMinute = { opensBeforeMinutes: 10, graceMinutes: 1, warnBeforeReleaseMinutes: 1 };
  let service: CheckInTimesService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CheckInTimesService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    setCheckInTimes(DEFAULT_CHECK_IN_TIMES);
    vi.useRealTimers();
  });

  it("takes up the API's times, asked signed out and without a toast", async () => {
    const loaded = service.load();

    const req = http.expectOne(url);
    expect(req.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush(oneMinute);
    await loaded;

    expect(CHECK_IN()).toEqual(oneMinute);
  });

  it('counts the window with them: a one-minute grace closes a minute after the start', async () => {
    const loaded = service.load();
    http.expectOne(url).flush(oneMinute);
    await loaded;

    const { opensAt, closesAt } = checkInWindow({
      startDateTime: '2026-10-05T10:00:00Z',
      endDateTime: '2026-10-05T11:00:00Z',
    });

    expect(opensAt.toISOString()).toBe('2026-10-05T09:50:00.000Z');
    expect(closesAt.toISOString()).toBe('2026-10-05T10:01:00.000Z');
  });

  it('keeps the defaults when the API fails, and lets the app start', async () => {
    const loaded = service.load();
    http.expectOne(url).flush(null, { status: 503, statusText: 'Service Unavailable' });

    await expect(loaded).resolves.toBeUndefined();
    expect(CHECK_IN()).toEqual(DEFAULT_CHECK_IN_TIMES);
  });

  it('stops waiting after a while, with the defaults', async () => {
    vi.useFakeTimers();
    const loaded = service.load();
    const req = http.expectOne(url);

    vi.advanceTimersByTime(CHECK_IN_TIMES_WAIT_MS);

    await expect(loaded).resolves.toBeUndefined();
    expect(req.cancelled).toBe(true);
    expect(CHECK_IN()).toEqual(DEFAULT_CHECK_IN_TIMES);
  });
});
