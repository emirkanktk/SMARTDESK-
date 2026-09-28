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
  let info: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
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
    info = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ReservationHubService, useValue: { startingSoon$: startingSoon } },
        { provide: NotificationService, useValue: { info } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
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
