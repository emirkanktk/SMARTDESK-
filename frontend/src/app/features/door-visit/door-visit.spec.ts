import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { DoorBooking } from '../../core/models/door-screen.model';
import { DoorVisit as DoorVisitData } from '../../core/models/door-visit.model';
import { DoorVisitService } from '../../core/services/door-visit.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReportIssue } from '../check-in/report-issue/report-issue';
import { DOOR_VISIT_REFRESH_MS, DoorVisit, checkInFor } from './door-visit';

// Local times, so what the page prints reads the same in every time zone the tests run in.
const at = (time: string) => new Date(`2026-10-07T${time}`);
const iso = (time: string) => at(time).toISOString();

const booking = (
  start: string,
  end: string,
  checkedIn = false,
  checkInNeeded = true,
): DoorBooking => ({
  startDateTime: iso(start),
  endDateTime: iso(end),
  checkInNeeded,
  checkedIn,
});

function visit(overrides: Partial<DoorVisitData> = {}): DoorVisitData {
  return {
    resourceName: 'Board Room',
    resourceTypeName: 'Meeting Room',
    location: '5th Floor',
    capacity: 8,
    description: null,
    features: ['TV', 'Whiteboard'],
    status: 'Active',
    checkInRequired: true,
    now: iso('09:55'),
    bookings: [booking('10:00', '11:00')],
    blocks: [],
    openIssues: [],
    ...overrides,
  };
}

describe('checkInFor', () => {
  it("offers the booking waiting for its check-in, whoever's it is", () => {
    expect(checkInFor([booking('10:00', '11:00')], at('09:55').getTime())).toEqual({
      kind: 'open',
      booking: booking('10:00', '11:00'),
      closesAt: at('10:15'),
    });
  });

  it('says one is checked in, or when the next one can be', () => {
    expect(
      checkInFor(
        [booking('10:00', '11:00', true), booking('14:00', '15:00')],
        at('10:20').getTime(),
      ),
    ).toEqual({ kind: 'checkedIn', booking: booking('10:00', '11:00', true) });
    // The one at 10:00 closed with nobody checked in: the next is at 14:00.
    expect(
      checkInFor([booking('10:00', '11:00'), booking('14:00', '15:00')], at('10:20').getTime()),
    ).toEqual({ kind: 'later', booking: booking('14:00', '15:00'), opensAt: at('13:50') });
  });

  it('has nothing to offer without a booking that needs a check-in', () => {
    expect(checkInFor([], at('10:00').getTime())).toEqual({ kind: 'none' });
    expect(checkInFor([booking('10:00', '11:00', false, false)], at('10:00').getTime())).toEqual({
      kind: 'none',
    });
  });
});

describe('DoorVisit', () => {
  let get: ReturnType<typeof vi.fn>;
  let checkIn: ReturnType<typeof vi.fn>;
  let report: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<DoorVisit>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(at('09:55'));
    get = vi.fn(() => of(visit()));
    checkIn = vi.fn(() => of(booking('10:00', '11:00', true)));
    report = vi.fn((_resourceId: string, _code: string, body: { description: string }) =>
      of({
        id: 'iss-new',
        resourceId: 'res-1',
        resourceName: 'Board Room',
        category: 'Cleaning',
        description: body.description,
        isBlocking: false,
        status: 'Open',
        hasPhoto: false,
        reportedByUserId: null,
        reportedByName: null,
        createdAt: iso('09:55'),
        resolvedAt: null,
      }),
    );
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: DoorVisitService, useValue: { get, checkIn, report } },
        { provide: NotificationService, useValue: { success } },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  /** `null`: opened without the code, e.g. from a bookmark. */
  async function render(code: string | null = 'c0de'): Promise<void> {
    fixture = TestBed.createComponent(DoorVisit);
    fixture.componentRef.setInput('resourceId', 'res-1');
    fixture.componentRef.setInput('code', code ?? undefined);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  function button(label: string): HTMLButtonElement {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === label,
    )!;
  }

  it('takes the code out of the address, and opens the room with it', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await render();

    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { code: null }, replaceUrl: true }),
    );
    expect(get).toHaveBeenCalledWith('res-1', 'c0de', expect.any(String));
    expect(element.querySelector('h1')!.textContent).toBe('Board Room');
    expect(text()).toContain('5th Floor · 8 people · TV, Whiteboard');
    expect(text()).toContain('Free now');
    expect(text()).toContain('Board Room is free until 10:00.');
  });

  it('asks for the QR code when opened without one, and asks the API nothing', async () => {
    await render(null);

    expect(text()).toContain("This page opens from the QR code at a resource's door.");
    expect(get).not.toHaveBeenCalled();
  });

  it("checks in the booking waiting for it, whoever's it is", async () => {
    await render();
    expect(text()).toContain(
      'The booking from 10:00 to 11:00 is waiting for its check-in, until 10:15.',
    );

    button('Check in').click();
    await fixture.whenStable();

    expect(checkIn).toHaveBeenCalledWith('res-1', 'c0de');
    expect(text()).toContain(
      'The booking from 10:00 to 11:00 is checked in, so the room is kept for it.',
    );
    expect(success).toHaveBeenCalledWith(
      'Checked in. Board Room is kept for the booking until 11:00.',
    );
    expect(element.querySelector('.check-in-button')).toBeNull();
  });

  it('says why a check-in was refused, and looks at the room again', async () => {
    const reason = 'No booking of Board Room is waiting for its check-in right now.';
    checkIn.mockReturnValue(throwError(() => new ApiError(409, 'Conflict', reason)));
    await render();

    button('Check in').click();
    await fixture.whenStable();

    expect(element.querySelector('.alert-error')!.textContent).toContain(reason);
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('says when the next check-in opens, or that the room needs none', async () => {
    get.mockReturnValue(of(visit({ bookings: [booking('14:00', '15:00')] })));
    await render();
    expect(text()).toContain('The next booking, at 14:00, can be checked in from 13:50.');

    get.mockReturnValue(of(visit({ checkInRequired: false, bookings: [] })));
    await render();
    expect(text()).toContain("Board Room doesn't need a check-in.");
    expect(text()).toContain('Board Room is free for the rest of the day.');
  });

  it("says a code the room doesn't take isn't valid", async () => {
    get.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(text()).toContain("This QR code isn't valid any more. Scan the code at the door again.");
  });

  it('keeps showing the room when a later look fails, and stops asking with a dead code', async () => {
    await render();
    get.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));

    vi.advanceTimersByTime(DOOR_VISIT_REFRESH_MS);
    await fixture.whenStable();

    expect(element.querySelector('h1')!.textContent).toBe('Board Room');
    expect(text()).toContain("What's shown here may be out of date.");
    vi.advanceTimersByTime(DOOR_VISIT_REFRESH_MS);
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('signs in back to the same page, with the code', async () => {
    await render();

    expect(element.querySelector('.sign-in a')!.getAttribute('href')).toBe(
      '/login?returnUrl=%2Fcheck-in%2Fres-1%3Fcode%3Dc0de',
    );
  });

  it('shows the problems reported there, and reports one with the code', async () => {
    get.mockReturnValue(
      of(
        visit({
          openIssues: [
            {
              id: 'iss-1',
              category: 'Display',
              description: 'No signal from HDMI',
              isBlocking: true,
              status: 'Open',
              createdAt: iso('08:00'),
            },
          ],
        }),
      ),
    );
    await render();
    expect(text()).toContain('No signal from HDMI');
    expect(text()).toContain("Can't be used");

    button('Report a problem').click();
    await fixture.whenStable();

    const form = fixture.debugElement.query(By.directive(ReportIssue))
      .componentInstance as ReportIssue;
    expect(form.doorCode()).toBe('c0de');
    expect(form.open()).toHaveLength(1);
  });

  it('sends a report with the code, without an account, and lists it', async () => {
    await render();
    button('Report a problem').click();
    await fixture.whenStable();

    const tile = [...element.querySelectorAll('.category-option')].find(
      (option) => option.textContent!.trim() === 'Cleaning',
    )!;
    tile.querySelector('input')!.click();
    const area = element.querySelector<HTMLTextAreaElement>('#issue-description')!;
    area.value = 'Coffee on the table';
    area.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('app-report-issue form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(report).toHaveBeenCalledWith('res-1', 'c0de', {
      category: 'Cleaning',
      description: 'Coffee on the table',
      isBlocking: false,
      photo: null,
    });
    expect(success).toHaveBeenCalledWith(
      'Thanks. The problem is reported, and whoever looks after Board Room is told.',
    );
    expect(element.querySelector('app-report-issue')).toBeNull();
    expect(text()).toContain('Coffee on the table');
  });
});
