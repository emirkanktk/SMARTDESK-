import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation } from '../../core/models/reservation.model';
import { ApprovalService } from '../../core/services/approval.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { Approvals } from './approvals';

describe('Approvals', () => {
  const request = (id: string, start: string, extra: Partial<Reservation> = {}) =>
    fakeReservation({
      id,
      resourceName: 'Company Car 1',
      resourceTypeName: 'Company Car',
      resourceLocation: 'Parking B1',
      status: 'Pending',
      startDateTime: start,
      endDateTime: start.replace('T07', 'T15'),
      requestedAt: '2026-10-05T06:30:00Z',
      note: 'Client visit in Gebze',
      ...extra,
    });
  const later = request('rsv-later', '2026-10-09T07:00:00Z', { userDisplayName: 'Mehmet Demir' });
  const sooner = request('rsv-sooner', '2026-10-06T07:00:00Z');

  let getWaiting: ReturnType<typeof vi.fn>;
  let approve: ReturnType<typeof vi.fn>;
  let decline: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let fixture: ComponentFixture<Approvals>;
  let element: HTMLElement;

  beforeEach(() => {
    getWaiting = vi.fn(() => of([later, sooner]));
    approve = vi.fn((id: string) => of({ ...sooner, id, status: 'Confirmed' }));
    decline = vi.fn((id: string, reason: string) =>
      of({ ...sooner, id, status: 'Cancelled', declineReason: reason }),
    );
    success = vi.fn();
    error = vi.fn();
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ApprovalService, useValue: { getWaiting, approve, decline } },
        { provide: ReservationHubService, useValue: hub },
        { provide: NotificationService, useValue: { success, error } },
      ],
    });
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Approvals);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function cards(): HTMLElement[] {
    return [...element.querySelectorAll<HTMLElement>('.request')];
  }

  function button(card: HTMLElement, text: string): HTMLButtonElement {
    return [...card.querySelectorAll('button')].find((b) => b.textContent!.trim() === text)!;
  }

  it('lists what waits, the soonest first, with who asked, when for and why', async () => {
    await render();

    expect(getWaiting).toHaveBeenCalledWith(true);
    expect(cards()).toHaveLength(2);
    const first = cards()[0].textContent!.replace(/\s+/g, ' ');
    expect(first).toContain('Company Car 1');
    expect(first).toContain('Company Car · Parking B1');
    expect(first).toContain('Asked by Ayşe Yılmaz');
    expect(first).toContain('Client visit in Gebze');
    expect(cards()[1].textContent).toContain('Mehmet Demir');
  });

  it('approves one: it goes from the list, and says so', async () => {
    await render();

    button(cards()[0], 'Approve').click();
    await fixture.whenStable();

    expect(approve).toHaveBeenCalledWith('rsv-sooner');
    expect(cards()).toHaveLength(1);
    expect(success).toHaveBeenCalledWith(
      expect.stringContaining('Approved: Company Car 1 for Ayşe Yılmaz'),
    );
  });

  it('declines one only with a reason, which is sent', async () => {
    await render();

    button(cards()[0], 'Decline').click();
    await fixture.whenStable();
    const confirm = button(cards()[0], 'Decline it');
    expect(confirm.disabled).toBe(true);

    const reason = cards()[0].querySelector('textarea')!;
    reason.value = 'The car is at the service that day';
    reason.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(button(cards()[0], 'Decline it').disabled).toBe(false);
    button(cards()[0], 'Decline it').click();
    await fixture.whenStable();

    expect(decline).toHaveBeenCalledWith('rsv-sooner', 'The car is at the service that day');
    expect(cards()).toHaveLength(1);
    expect(success).toHaveBeenCalledWith(
      expect.stringContaining("Declined: Company Car 1 for Ayşe Yılmaz. They're told why."),
    );
  });

  it('says why when another Admin decided first, and shows the list as it is now', async () => {
    approve.mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', "This booking isn't waiting for approval.")),
    );
    await render();
    getWaiting.mockReturnValue(of([later]));

    button(cards()[0], 'Approve').click();
    await fixture.whenStable();

    expect(error).toHaveBeenCalledWith("This booking isn't waiting for approval.");
    expect(getWaiting).toHaveBeenCalledTimes(2);
    expect(cards()).toHaveLength(1);
  });

  it('follows requests live: a new one comes in, a decided or cancelled one goes', async () => {
    await render();

    hub.created$.next(request('rsv-new', '2026-10-05T12:00:00Z'));
    await fixture.whenStable();
    expect(cards()).toHaveLength(3);
    expect(cards()[0].textContent).toContain('Company Car 1');

    hub.updated$.next({ ...later, status: 'Confirmed' });
    hub.cancelled$.next({ ...sooner, status: 'Cancelled' });
    await fixture.whenStable();
    expect(cards()).toHaveLength(1);

    hub.created$.next(fakeReservation({ id: 'rsv-booked' }));
    await fixture.whenStable();
    expect(cards()).toHaveLength(1);
  });

  it('says when nothing waits, and offers a retry when the list fails', async () => {
    getWaiting.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    getWaiting.mockReturnValueOnce(of([]));
    await render();

    expect(element.querySelector('[role=alert]')!.textContent).toContain(
      'The requests could not be loaded.',
    );
    [...element.querySelectorAll('button')]
      .find((b) => b.textContent!.includes('Try again'))!
      .click();
    await fixture.whenStable();

    expect(element.textContent).toContain('Nothing waits for approval.');
  });
});
