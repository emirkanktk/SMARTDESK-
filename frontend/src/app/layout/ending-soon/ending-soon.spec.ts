import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { Reservation } from '../../core/models/reservation.model';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { EndingSoon } from './ending-soon';

/** Stand-in for the browser's Notification, which jsdom doesn't have. */
class FakeNotification {
  static permission: NotificationPermission = 'default';
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

describe('EndingSoon', () => {
  const booking = (id: string, resourceName: string, end = '2026-10-05T11:00') =>
    fakeReservation({
      id,
      resourceName,
      startDateTime: new Date('2026-10-05T10:00').toISOString(),
      endDateTime: new Date(end).toISOString(),
    });

  let endingSoon: Subject<Reservation>;
  let updated: Subject<Reservation>;
  let cancelled: Subject<Reservation>;
  let extend: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<EndingSoon>;
  let element: HTMLElement;

  beforeEach(async () => {
    FakeNotification.permission = 'default';
    FakeNotification.shown = [];
    vi.stubGlobal('Notification', FakeNotification);
    endingSoon = new Subject();
    updated = new Subject();
    cancelled = new Subject();
    extend = vi.fn((id: string) => of(booking(id, 'Board Room', '2026-10-05T11:30')));

    TestBed.configureTestingModule({
      providers: [
        {
          provide: ReservationHubService,
          useValue: { endingSoon$: endingSoon, updated$: updated, cancelled$: cancelled },
        },
        {
          provide: ReservationService,
          useValue: { getExtensionOptions: () => of({ minutes: [15, 30] }), extend },
        },
        { provide: NotificationService, useValue: { success: vi.fn() } },
      ],
    });
    fixture = TestBed.createComponent(EndingSoon);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => vi.unstubAllGlobals());

  const title = () => element.querySelector('h2')?.textContent?.replace(/\s+/g, ' ').trim() ?? null;

  async function arrive(reservation: Reservation): Promise<void> {
    endingSoon.next(reservation);
    await fixture.whenStable();
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === label,
    )!;
  }

  it('shows nothing until a booking is about to end', () => {
    expect(element.querySelector('section')).toBeNull();
  });

  it('asks whether the booking needs longer, with the ways to extend it', async () => {
    await arrive(booking('rsv-1', 'Board Room'));

    expect(title()).toBe('Board Room ends at 11:00');
    expect(element.textContent).toContain('Need more time?');
    expect(button('+15 min')).toBeDefined();
    expect(button('+30 min')).toBeDefined();
  });

  it('goes once the booking is extended', async () => {
    await arrive(booking('rsv-1', 'Board Room'));
    button('+30 min').click();
    await fixture.whenStable();

    expect(extend).toHaveBeenCalledWith('rsv-1', 30);
    expect(element.querySelector('section')).toBeNull();
  });

  it('shows one at a time, the next after "Not now"', async () => {
    await arrive(booking('rsv-1', 'Board Room'));
    await arrive(booking('rsv-2', 'Desk 12'));
    await arrive(booking('rsv-1', 'Board Room')); // asked twice: shown once

    expect(title()).toBe('Board Room ends at 11:00');
    button('Not now').click();
    await fixture.whenStable();
    expect(title()).toBe('Desk 12 ends at 11:00');
    button('Not now').click();
    await fixture.whenStable();
    expect(element.querySelector('section')).toBeNull();
  });

  it('goes by itself when the booking is extended or cancelled elsewhere', async () => {
    await arrive(booking('rsv-1', 'Board Room'));
    await arrive(booking('rsv-2', 'Desk 12'));

    updated.next(booking('rsv-1', 'Board Room')); // same end: still asked
    await fixture.whenStable();
    expect(title()).toBe('Board Room ends at 11:00');

    updated.next(booking('rsv-1', 'Board Room', '2026-10-05T11:15'));
    await fixture.whenStable();
    expect(title()).toBe('Desk 12 ends at 11:00');

    cancelled.next({ ...booking('rsv-2', 'Desk 12'), status: 'Cancelled' });
    await fixture.whenStable();
    expect(element.querySelector('section')).toBeNull();
  });

  it('also shows a desktop notification when allowed, once per booking', async () => {
    FakeNotification.permission = 'granted';
    const focus = vi.spyOn(window, 'focus').mockImplementation(() => undefined);
    await arrive(booking('rsv-1', 'Board Room'));
    await arrive(booking('rsv-1', 'Board Room'));

    expect(FakeNotification.shown).toHaveLength(1);
    const [desktop] = FakeNotification.shown;
    expect(desktop.title).toBe('Your booking ends soon');
    expect(desktop.options).toEqual({
      body: 'Board Room ends at 11:00. Need more time? Extend it in SmartDesk.',
      tag: 'ending-rsv-1',
    });
    desktop.onclick!();
    expect(focus).toHaveBeenCalled();
    expect(desktop.close).toHaveBeenCalled();
  });
});
