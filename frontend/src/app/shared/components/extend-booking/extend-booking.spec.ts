import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { ExtensionOptions, Reservation } from '../../../core/models/reservation.model';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationService } from '../../../core/services/reservation.service';
import { fakeReservation } from '../../../../testing/reservation-fixtures';
import { ExtendBooking } from './extend-booking';

describe('ExtendBooking', () => {
  // Board Room until 11:00 local time.
  const booking = fakeReservation({
    id: 'rsv-1',
    resourceName: 'Board Room',
    startDateTime: new Date('2026-10-05T10:00').toISOString(),
    endDateTime: new Date('2026-10-05T11:00').toISOString(),
  });

  let getExtensionOptions: ReturnType<typeof vi.fn>;
  let extend: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let extended: Reservation[];
  let fixture: ComponentFixture<ExtendBooking>;
  let element: HTMLElement;

  beforeEach(() => {
    getExtensionOptions = vi.fn(() => of({ minutes: [15, 30, 60] }));
    extend = vi.fn((_: string, minutes: number) =>
      of({
        ...booking,
        endDateTime: new Date(
          new Date(booking.endDateTime).getTime() + minutes * 60_000,
        ).toISOString(),
      }),
    );
    success = vi.fn();
    extended = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: ReservationService, useValue: { getExtensionOptions, extend } },
        { provide: NotificationService, useValue: { success } },
      ],
    });
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(ExtendBooking);
    fixture.componentRef.setInput('reservation', booking);
    fixture.componentInstance.extended.subscribe((b) => extended.push(b));
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const labels = () =>
    Array.from(element.querySelectorAll('button')).map((b) => b.textContent!.trim());

  it('offers what the room is still free for, an hour as "+1 h"', async () => {
    await render();

    expect(getExtensionOptions).toHaveBeenCalledWith('rsv-1');
    expect(labels()).toEqual(['+15 min', '+30 min', '+1 h']);
  });

  it('extends with a click and says until when', async () => {
    await render();
    element.querySelectorAll('button')[1].click();
    await fixture.whenStable();

    expect(extend).toHaveBeenCalledExactlyOnceWith('rsv-1', 30);
    expect(success).toHaveBeenCalledWith('Board Room is now booked until 11:30.');
    expect(extended.map((b) => b.endDateTime)).toEqual([
      new Date('2026-10-05T11:30').toISOString(),
    ]);
  });

  it('says so when it cannot run any longer', async () => {
    getExtensionOptions.mockReturnValue(of({ minutes: [] }));
    await render();

    expect(labels()).toEqual([]);
    expect(element.textContent).toContain(
      "Board Room is taken right after, so this can't run any longer.",
    );
  });

  it('says it is checking while the options load', async () => {
    const options = new Subject<ExtensionOptions>();
    getExtensionOptions.mockReturnValue(options);
    await render();
    expect(element.querySelector('[role="status"]')!.textContent).toContain('Checking how long');

    options.next({ minutes: [15] });
    await fixture.whenStable();
    expect(labels()).toEqual(['+15 min']);
  });

  it("shows the API's reason when the extra time was taken meanwhile", async () => {
    extend.mockReturnValue(
      throwError(
        () =>
          new ApiError(
            409,
            'Conflict',
            "Board Room is reserved right after, so this can't run 15 minutes longer.",
          ),
      ),
    );
    await render();
    element.querySelector('button')!.click();
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      "can't run 15 minutes longer",
    );
    expect(extended).toEqual([]);
    expect(element.querySelector('button')!.disabled).toBe(false);
  });

  it('says so when the options could not be loaded', async () => {
    getExtensionOptions.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    await render();

    expect(element.querySelector('[role="alert"]')!.textContent).toBe(
      'It could not be extended. Try again.',
    );
  });
});
