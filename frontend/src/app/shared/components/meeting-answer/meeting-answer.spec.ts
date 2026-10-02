import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { AttendeeResponse, Reservation } from '../../../core/models/reservation.model';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ReservationService } from '../../../core/services/reservation.service';
import { fakeReservation } from '../../../../testing/reservation-fixtures';
import { MeetingAnswer } from './meeting-answer';

describe('MeetingAnswer', () => {
  const me = 'u-mehmet';
  const meeting = (response: AttendeeResponse = 'Pending') =>
    fakeReservation({
      id: 'rsv-20',
      userId: 'u-zeynep',
      userDisplayName: 'Zeynep Kaya',
      attendees: [
        { id: me, name: 'Mehmet Demir', response },
        { id: 'u-ayse', name: 'Ayşe Yılmaz', response: 'Accepted' },
      ],
    });

  let respond: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let answered: Reservation[];
  let fixture: ComponentFixture<MeetingAnswer>;
  let element: HTMLElement;

  beforeEach(() => {
    respond = vi.fn((_: string, body: { response: AttendeeResponse }) =>
      of(meeting(body.response)),
    );
    success = vi.fn();
    answered = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: ReservationService, useValue: { respond } },
        { provide: AuthService, useValue: { currentUser: signal({ id: me }) } },
        { provide: NotificationService, useValue: { success } },
      ],
    });
  });

  async function render(reservation: Reservation): Promise<void> {
    fixture = TestBed.createComponent(MeetingAnswer);
    fixture.componentRef.setInput('reservation', reservation);
    fixture.componentInstance.answered.subscribe((r) => {
      answered.push(r);
      fixture.componentRef.setInput('reservation', r); // as the pages do
    });
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ').trim();

  function button(label: string): HTMLButtonElement | undefined {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === label,
    );
  }

  async function click(label: string): Promise<void> {
    button(label)!.click();
    await fixture.whenStable();
  }

  async function typeReason(value: string): Promise<void> {
    const textarea = element.querySelector('textarea')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function send(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('shows nothing to someone who is not on the list', async () => {
    await render(
      fakeReservation({ attendees: [{ id: 'u-ayse', name: 'Ayşe', response: 'Pending' }] }),
    );

    expect(element.children).toHaveLength(0);
  });

  it('asks whether they are coming, and sends a yes to the organizer', async () => {
    await render(meeting());
    expect(text()).toContain('Will you come?');

    await click("I'm coming");

    expect(respond).toHaveBeenCalledExactlyOnceWith('rsv-20', { response: 'Accepted' });
    expect(success).toHaveBeenCalledWith('Your answer was sent to Zeynep Kaya.');
    expect(answered.map((r) => r.attendees[0].response)).toEqual(['Accepted']);
    expect(text()).toContain("You said you're coming.");
  });

  it('wants a reason to say no, and sends it trimmed', async () => {
    await render(meeting());
    await click("I can't come");
    expect(element.querySelector('label')!.textContent).toContain("Why can't you come?");
    expect(text()).toContain('Only the organizer sees this.');

    await send();
    expect(element.querySelector('[role="alert"]')!.textContent).toBe("Say why you can't come.");
    expect(respond).not.toHaveBeenCalled();

    await typeReason('  With a customer until 11:00  ');
    await send();

    expect(respond).toHaveBeenCalledExactlyOnceWith('rsv-20', {
      response: 'Declined',
      reason: 'With a customer until 11:00',
    });
    expect(text()).toContain("You said you can't come.");
  });

  it('goes back to the question from the reason', async () => {
    await render(meeting());
    await click("I can't come");
    await click('Back');

    expect(element.querySelector('form')).toBeNull();
    expect(button("I'm coming")).toBeDefined();
  });

  it('lets an answer be changed, without offering the same one again', async () => {
    await render(meeting('Accepted'));
    expect(button("I'm coming")).toBeUndefined();

    await click('Change your answer');

    expect(button("I'm coming")!.disabled).toBe(true);
    expect(button("I can't come")!.disabled).toBe(false);
    await click('Back');
    expect(text()).toContain("You said you're coming.");
  });

  it("shows the API's reason when the answer is refused", async () => {
    respond.mockReturnValue(
      throwError(
        () =>
          new ApiError(
            409,
            'Conflict',
            'This meeting was cancelled, so there is nothing to answer.',
          ),
      ),
    );
    await render(meeting());
    await click("I'm coming");

    expect(element.querySelector('[role="alert"]')!.textContent).toBe(
      'This meeting was cancelled, so there is nothing to answer.',
    );
    expect(answered).toEqual([]);
    expect(button("I'm coming")!.disabled).toBe(false);
  });

  it("puts a 400's message for the reason under it", async () => {
    respond.mockReturnValue(
      throwError(
        () => new ApiError(400, 'Invalid', null, { Reason: ['Use at most 500 characters.'] }),
      ),
    );
    await render(meeting());
    await click("I can't come");
    await typeReason('Too long');
    await send();

    expect(element.querySelector('[role="alert"]')!.textContent).toBe(
      'Use at most 500 characters.',
    );
    expect(element.querySelector('form')).not.toBeNull();
  });

  it('says so when the answer never reached the server', async () => {
    respond.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    await render(meeting());
    await click("I'm coming");

    expect(element.querySelector('[role="alert"]')!.textContent).toBe(
      'Your answer could not be sent. Try again.',
    );
  });

  it('thanks without a name when the organizer is unknown', async () => {
    respond.mockReturnValue(of({ ...meeting('Accepted'), userDisplayName: null }));
    await render(meeting());
    await click("I'm coming");

    expect(success).toHaveBeenCalledWith('Your answer was sent.');
  });
  describe('for the dates of a meeting that repeats (ADR-0035)', () => {
    it('answers for this date and every later one, and says so', async () => {
      fixture = TestBed.createComponent(MeetingAnswer);
      fixture.componentRef.setInput('reservation', meeting());
      fixture.componentRef.setInput('following', true);
      element = fixture.nativeElement;
      await fixture.whenStable();

      expect(text()).toContain('Your answer is for this date and every later one.');
      button("I can't come")!.click();
      await fixture.whenStable();
      const reason = element.querySelector<HTMLTextAreaElement>('textarea')!;
      reason.value = 'On leave';
      reason.dispatchEvent(new Event('input'));
      element.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-20', {
        response: 'Declined',
        reason: 'On leave',
        following: true,
      });
    });

    it('answers for one date only, unless asked for more', async () => {
      await render(meeting());

      expect(text()).not.toContain('every later one');
      button("I'm coming")!.click();
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-20', { response: 'Accepted' });
    });
  });
});
