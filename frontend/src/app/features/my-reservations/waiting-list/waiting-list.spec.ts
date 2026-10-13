import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { Reservation } from '../../../core/models/reservation.model';
import { WaitlistEntry } from '../../../core/models/waitlist.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationHubService } from '../../../core/services/reservation-hub.service';
import { WaitlistService } from '../../../core/services/waitlist.service';
import { OWNER_ID, fakeReservation } from '../../../../testing/reservation-fixtures';
import { WaitingList } from './waiting-list';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('WaitingList', () => {
  const entry = (overrides: Partial<WaitlistEntry> = {}): WaitlistEntry => ({
    id: 'wait-1',
    resourceId: 'res-board',
    resourceName: 'Board Room',
    resourceLocation: '5th Floor',
    startDateTime: utc('2026-10-07T14:00'),
    endDateTime: utc('2026-10-07T15:00'),
    createdAt: utc('2026-10-05T09:00'),
    position: 2,
    ...overrides,
  });

  let getMine: ReturnType<typeof vi.fn>;
  let leave: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let hub: { created$: Subject<Reservation>; reconnected$: Subject<void> };
  let fixture: ComponentFixture<WaitingList>;
  let element: HTMLElement;

  beforeEach(() => {
    getMine = vi.fn(() => of([entry()]));
    leave = vi.fn(() => of(undefined));
    success = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    hub = { created$: new Subject(), reconnected$: new Subject() };

    TestBed.configureTestingModule({
      providers: [
        { provide: WaitlistService, useValue: { getMine, leave } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser: signal({ id: OWNER_ID }) } },
        { provide: NotificationService, useValue: { success } },
      ],
    });
  });

  afterEach(() => confirmSpy.mockRestore());

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(WaitingList);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  const leaveButton = () => element.querySelector<HTMLButtonElement>('li button')!;

  it('lists what the user waits for, with their place in line', async () => {
    await render();

    expect(text(element.querySelector('h2')!)).toBe('Waiting for');
    const [item] = [...element.querySelectorAll('li')];
    expect(text(item.querySelector('.what')!)).toBe(
      'Board Room Wed 7 Oct, 14:00-15:00 · 5th Floor',
    );
    expect(text(item.querySelector('.position')!)).toBe('Number 2 in line');
    expect(leaveButton().getAttribute('aria-label')).toBe(
      'Leave the waiting list for Board Room, Wed 7 Oct, 14:00-15:00',
    );
  });

  it('shows nothing when the user waits for nothing', async () => {
    getMine.mockReturnValue(of([]));
    await render();

    expect(element.querySelector('section')).toBeNull();
  });

  it('leaves the line once the user confirms', async () => {
    confirmSpy.mockReturnValue(false);
    await render();
    leaveButton().click();
    expect(confirmSpy).toHaveBeenCalledWith('Stop waiting for Board Room, Wed 7 Oct, 14:00-15:00?');
    expect(leave).not.toHaveBeenCalled();

    confirmSpy.mockReturnValue(true);
    leaveButton().click();
    await fixture.whenStable();

    expect(leave).toHaveBeenCalledWith('wait-1');
    expect(element.querySelector('section')).toBeNull();
    expect(success).toHaveBeenCalledWith(
      "You're no longer waiting for Board Room, Wed 7 Oct, 14:00-15:00.",
    );
  });

  it('shows the list as it is when the entry went meanwhile', async () => {
    leave.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();
    getMine.mockReturnValue(of([]));

    leaveButton().click();
    await fixture.whenStable();

    expect(getMine).toHaveBeenCalledTimes(2);
    expect(element.querySelector('section')).toBeNull();
  });

  it("loads again when a booking of the user's comes in, as one from the line does, and after a reconnect", async () => {
    await render();
    getMine.mockReturnValue(of([]));

    hub.created$.next(fakeReservation({ userId: 'someone-else' }));
    await fixture.whenStable();
    expect(getMine).toHaveBeenCalledTimes(1);

    hub.created$.next(fakeReservation({ userId: OWNER_ID }));
    await fixture.whenStable();
    expect(getMine).toHaveBeenCalledTimes(2);
    expect(element.querySelector('section')).toBeNull();

    hub.reconnected$.next();
    await fixture.whenStable();
    expect(getMine).toHaveBeenCalledTimes(3);
  });
});
