import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { Reservation } from '../models/reservation.model';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { NotificationService } from './notification.service';
import { REMINDER_TOAST_MS, ReminderAlertsService } from './reminder-alerts.service';
import { ReservationHubService } from './reservation-hub.service';

/** Stand-in for the browser's Notification, which jsdom doesn't have. */
class FakeNotification {
  static permission: NotificationPermission = 'default';
  static requestPermission = vi.fn(async () => FakeNotification.permission);
  static shown: FakeNotification[] = [];
  onclick: (() => void) | null = null;
  close = vi.fn();

  constructor(
    readonly title: string,
    readonly options: NotificationOptions,
  ) {
    FakeNotification.shown.push(this);
  }
}

describe('ReminderAlertsService', () => {
  const NOW = new Date('2026-10-07T13:45'); // local time, 15 minutes before 14:00
  let startingSoon: Subject<Reservation>;
  let released: Subject<Reservation>;
  let releaseWarning: Subject<Reservation>;
  let info: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let service: ReminderAlertsService;

  const meeting = fakeReservation({
    id: 'rsv-7',
    resourceName: 'Meeting Room A',
    startDateTime: new Date('2026-10-07T14:00').toISOString(),
    reminderMinutes: 15,
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    FakeNotification.permission = 'default';
    FakeNotification.requestPermission.mockClear();
    FakeNotification.shown = [];
    vi.stubGlobal('Notification', FakeNotification);
    startingSoon = new Subject();
    released = new Subject();
    releaseWarning = new Subject();
    info = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ReservationHubService,
          useValue: {
            startingSoon$: startingSoon,
            released$: released,
            releaseWarning$: releaseWarning,
          },
        },
        { provide: NotificationService, useValue: { info } },
      ],
    });
    const router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    service = TestBed.inject(ReminderAlertsService);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows a long-lived toast saying what starts and when', () => {
    service.start();
    startingSoon.next(meeting);

    expect(info).toHaveBeenCalledExactlyOnceWith(
      'Meeting Room A starts in 15 min, at 14:00.',
      REMINDER_TOAST_MS,
    );
  });

  it('says "starts now" when the reminder arrives at the start', () => {
    vi.setSystemTime(new Date('2026-10-07T14:00:10'));
    service.start();
    startingSoon.next(meeting);

    expect(info).toHaveBeenCalledWith('Meeting Room A starts now, at 14:00.', REMINDER_TOAST_MS);
  });

  it('reminds a booking that needs a check-in to have it done, and by when', () => {
    service.start();
    startingSoon.next({
      ...meeting,
      endDateTime: new Date('2026-10-07T15:00').toISOString(),
      checkInRequired: true,
    });

    expect(info).toHaveBeenCalledExactlyOnceWith(
      "Meeting Room A starts in 15 min, at 14:00. Check in at the door with the QR code by 14:15, or it's released.",
      REMINDER_TOAST_MS,
    );
  });

  it('warns five minutes before a release, while there is still time to get to the door', () => {
    FakeNotification.permission = 'granted';
    service.start();
    releaseWarning.next({
      ...meeting,
      startDateTime: new Date('2026-10-07T13:35').toISOString(),
      endDateTime: new Date('2026-10-07T14:35').toISOString(),
      checkInRequired: true,
    });

    const message =
      'Nobody has checked in to Meeting Room A yet. Scan the QR code at the door by 13:50, or the booking is released.';
    expect(info).toHaveBeenCalledExactlyOnceWith(message, REMINDER_TOAST_MS);
    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('Check in now, or the room is released');
    expect(desktop.options).toEqual({ body: message, tag: 'release-warning-rsv-7' });
    desktop.onclick!();
    expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
  });

  it('tells the owner when a booking was released because nobody checked in', () => {
    FakeNotification.permission = 'granted';
    service.start();
    released.next({ ...meeting, status: 'Cancelled', releasedAt: '2026-10-07T11:15:00Z' });

    const message =
      'Your booking of Meeting Room A at 14:00 was released because nobody checked in.';
    expect(info).toHaveBeenCalledExactlyOnceWith(message, REMINDER_TOAST_MS);
    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('Booking released');
    expect(desktop.options).toEqual({ body: message, tag: 'released-rsv-7' });

    desktop.onclick!();
    expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
  });

  it('adds a desktop notification when allowed, which opens the reservation', () => {
    FakeNotification.permission = 'granted';
    service.start();
    startingSoon.next(meeting);

    expect(FakeNotification.shown).toHaveLength(1);
    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('Reservation starting soon');
    expect(desktop.options).toEqual({
      body: 'Meeting Room A starts in 15 min, at 14:00.',
      tag: 'reminder-rsv-7',
    });

    desktop.onclick!();
    expect(navigate).toHaveBeenCalledWith(['/reservations', 'rsv-7', 'edit']);
    expect(desktop.close).toHaveBeenCalled();
  });

  it('shows no desktop notification unless it was allowed', () => {
    for (const permission of ['default', 'denied'] as const) {
      FakeNotification.permission = permission;
      service.start();
      startingSoon.next(meeting);
    }

    expect(FakeNotification.shown).toHaveLength(0);
    expect(info).toHaveBeenCalledTimes(2);
  });

  it('listens once, however often it is started', () => {
    service.start();
    service.start();
    startingSoon.next(meeting);

    expect(info).toHaveBeenCalledTimes(1);
  });

  it('asks for desktop notifications only while the browser has not been asked', () => {
    service.requestPermissionIfUnasked();
    expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);

    for (const permission of ['granted', 'denied'] as const) {
      FakeNotification.permission = permission;
      service.requestPermissionIfUnasked();
    }
    expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);
  });

  it('copes with a browser that has no notifications at all', () => {
    vi.stubGlobal('Notification', undefined);

    expect(service.permission()).toBe('unsupported');
    expect(() => service.requestPermissionIfUnasked()).not.toThrow();
    service.start();
    startingSoon.next(meeting);
    expect(info).toHaveBeenCalledTimes(1);
  });
});
