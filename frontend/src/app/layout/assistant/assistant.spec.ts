import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  AssistantMessage,
  AssistantProposal,
  AssistantReply,
} from '../../core/models/assistant.model';
import { CurrentUser } from '../../core/models/auth.model';
import { AssistantService } from '../../core/services/assistant.service';
import { AuthService } from '../../core/services/auth.service';
import { ReservationService } from '../../core/services/reservation.service';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { Assistant } from './assistant';

describe('Assistant', () => {
  const currentUser = signal<CurrentUser | null>(null);
  let status: Observable<{ enabled: boolean }>;
  let ask: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let cancel: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Assistant>;
  let element: HTMLElement;

  // Tuesday 6 October 2026, 10:00-11:00 on this machine's clock, whatever its zone.
  const start = new Date(2026, 9, 6, 10, 0);
  const end = new Date(2026, 9, 6, 11, 0);
  const booking: AssistantProposal = {
    kind: 'book',
    resourceId: 'res-1',
    reservationId: null,
    resourceName: 'Room A',
    startDateTime: start.toISOString(),
    endDateTime: end.toISOString(),
    note: 'Sprint planning',
  };
  const cancellation: AssistantProposal = {
    ...booking,
    kind: 'cancel',
    reservationId: 'rsv-1',
    note: null,
  };

  const reply = (text: string, proposals: AssistantProposal[] = []): Observable<AssistantReply> =>
    of({ reply: text, proposals });

  beforeEach(async () => {
    currentUser.set({
      id: 'u-1',
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      email: 'ayse@smartdesk.local',
      roles: ['Employee'],
    });
    status = of({ enabled: true });
    ask = vi.fn(() => reply('Hello!'));
    create = vi.fn(() => of(fakeReservation()));
    cancel = vi.fn(() => of(fakeReservation({ status: 'Cancelled' })));

    await TestBed.configureTestingModule({
      imports: [Assistant],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser } },
        { provide: AssistantService, useValue: { getStatus: () => status, ask } },
        { provide: ReservationService, useValue: { create, cancel } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    element?.remove();
    vi.restoreAllMocks();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Assistant);
    element = fixture.nativeElement;
    document.body.appendChild(element);
    await fixture.whenStable();
  }

  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };
  const launcher = () => element.querySelector<HTMLButtonElement>('.launcher');
  const panel = () => element.querySelector<HTMLElement>('[role="dialog"]');
  const input = () => element.querySelector<HTMLTextAreaElement>('textarea')!;
  const buttonNamed = (name: string) =>
    Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === name,
    );
  const sent = (call = -1): AssistantMessage[] => ask.mock.calls.at(call)![0];

  async function openPanel(): Promise<void> {
    await render();
    launcher()!.click();
    await settle();
  }

  async function type(text: string, key = 'Enter', shiftKey = false): Promise<void> {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    input().dispatchEvent(new KeyboardEvent('keydown', { key, shiftKey, bubbles: true }));
    await settle();
  }

  it('shows nothing when the server has no assistant, or does not say', async () => {
    status = of({ enabled: false });
    await render();
    expect(launcher()).toBeNull();

    status = throwError(() => new ApiError(404, 'Not Found'));
    await render();
    expect(launcher()).toBeNull();
  });

  it('opens from its button, and Escape closes it and gives focus back', async () => {
    await openPanel();

    expect(launcher()!.getAttribute('aria-expanded')).toBe('true');
    expect(panel()!.getAttribute('aria-labelledby')).toBe('assistant-title');
    expect(document.activeElement).toBe(input());

    panel()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(panel()).toBeNull();
    expect(launcher()!.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(launcher());
  });

  it('greets the user by name and sends a suggestion when it is picked', async () => {
    await openPanel();

    expect(element.querySelector('.log')!.textContent).toContain('Hi Ayşe!');
    const suggestion = element.querySelector<HTMLButtonElement>('.suggestion')!;
    const text = suggestion.textContent!.trim();
    suggestion.click();
    await settle();

    expect(sent()).toEqual([{ role: 'user', text }]);
    expect(element.querySelector('.from-user')!.textContent).toContain(text);
    expect(element.querySelector('.suggestion')).toBeNull();
  });

  it('sends the whole conversation each time and shows lists as lists', async () => {
    ask.mockReturnValueOnce(reply('Two are free:\n- Room A, 8 people\n- Room B, 4 people'));
    await openPanel();

    await type('  A room for 4 tomorrow at 10  ');
    expect(sent()).toEqual([{ role: 'user', text: 'A room for 4 tomorrow at 10' }]);
    expect(input().value).toBe('');
    expect(
      Array.from(element.querySelectorAll('.from-assistant li')).map((li) =>
        li.textContent!.trim(),
      ),
    ).toEqual(['Room A, 8 people', 'Room B, 4 people']);

    await type('The smaller one');
    expect(sent()).toEqual([
      { role: 'user', text: 'A room for 4 tomorrow at 10' },
      { role: 'assistant', text: 'Two are free:\n- Room A, 8 people\n- Room B, 4 people' },
      { role: 'user', text: 'The smaller one' },
    ]);
  });

  it('starts a new line on Shift+Enter instead of sending', async () => {
    await openPanel();

    await type('A room', 'Enter', true);

    expect(ask).not.toHaveBeenCalled();
  });

  it('shows that it is thinking, and takes no second question meanwhile', async () => {
    const pending = new Subject<AssistantReply>();
    ask.mockReturnValueOnce(pending);
    await openPanel();

    await type('A room for 4');
    expect(element.querySelector('.thinking')!.textContent).toContain('Thinking…');
    expect(element.querySelector<HTMLButtonElement>('.send')!.disabled).toBe(true);
    await type('Another question');
    expect(ask).toHaveBeenCalledTimes(1);

    pending.next({ reply: 'Room A is free.', proposals: [] });
    pending.complete();
    await settle();
    expect(element.querySelector('.thinking')).toBeNull();
  });

  it('books a proposed booking only when its button is pressed, and tells the assistant', async () => {
    ask.mockReturnValueOnce(reply('Room A is free. The card is ready.', [booking]));
    await openPanel();
    await type('Book Room A tomorrow at 10');

    const card = element.querySelector('app-assistant-card')!;
    expect(card.textContent).toContain('Room A');
    expect(card.textContent).toContain('Tue 6 Oct, 10:00-11:00');
    expect(card.textContent).toContain('Sprint planning');
    expect(create).not.toHaveBeenCalled();

    buttonNamed('Reserve')!.click();
    await settle();

    expect(create).toHaveBeenCalledWith({
      resourceId: 'res-1',
      startDateTime: booking.startDateTime,
      endDateTime: booking.endDateTime,
      note: 'Sprint planning',
      reminderMinutes: null,
    });
    expect(card.textContent).toContain('Booked');
    expect(card.querySelector('a')!.getAttribute('href')).toBe('/reservations/mine');
    expect(element.querySelector('.event')!.textContent).toContain(
      'Booked: Room A, Tue 6 Oct, 10:00-11:00',
    );

    await type('Thanks');
    expect(sent().at(-2)).toEqual({ role: 'user', text: 'Booked: Room A, Tue 6 Oct, 10:00-11:00' });
  });

  it("says why when the server refuses a card's booking", async () => {
    ask.mockReturnValueOnce(reply('The card is ready.', [booking]));
    create.mockReturnValueOnce(
      throwError(
        () => new ApiError(409, 'Conflict', 'Room A is already reserved for part of that time.'),
      ),
    );
    await openPanel();
    await type('Book Room A');

    buttonNamed('Reserve')!.click();
    await settle();

    const card = element.querySelector('app-assistant-card')!;
    expect(card.querySelector('.field-error')!.textContent).toContain(
      'Room A is already reserved for part of that time.',
    );
    expect(buttonNamed('Reserve')!.disabled).toBe(false);
    expect(element.querySelector('.event')).toBeNull();
  });

  it('cancels a reservation only after the user confirms it', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);
    ask.mockReturnValueOnce(reply('Shall I cancel it?', [cancellation]));
    await openPanel();
    await type('Cancel my Room A booking');

    buttonNamed('Cancel reservation')!.click();
    await settle();
    expect(confirmSpy).toHaveBeenCalledWith(
      'Cancel your Room A reservation for Tue 6 Oct, 10:00-11:00?',
    );
    expect(cancel).not.toHaveBeenCalled();

    confirmSpy.mockReturnValue(true);
    buttonNamed('Cancel reservation')!.click();
    await settle();
    expect(cancel).toHaveBeenCalledWith('rsv-1');
    expect(element.querySelector('app-assistant-card')!.textContent).toContain('Cancelled');
    expect(element.querySelector('.event')!.textContent).toContain('Cancelled: Room A');
  });

  it("shows the server's reason when it can't answer, and asks again on Try again", async () => {
    ask.mockReturnValueOnce(
      throwError(
        () =>
          new ApiError(
            503,
            'Service unavailable',
            "The assistant can't answer right now. Try again in a moment.",
          ),
      ),
    );
    await openPanel();
    await type('A room for 4');

    const alert = element.querySelector('[role="alert"]')!;
    expect(alert.textContent).toContain("The assistant can't answer right now.");
    buttonNamed('Try again')!.click();
    await settle();

    expect(ask).toHaveBeenCalledTimes(2);
    expect(sent()).toEqual([{ role: 'user', text: 'A room for 4' }]);
    expect(element.querySelector('[role="alert"]')).toBeNull();
    expect(element.querySelector('.from-assistant:last-of-type')!.textContent).toContain('Hello!');
  });

  it('offers no Try again when the question itself was refused', async () => {
    ask.mockReturnValueOnce(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            Messages: ['This conversation is at its limit of 40 messages. Start a new one.'],
          }),
      ),
    );
    await openPanel();
    await type('One more');

    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      'This conversation is at its limit of 40 messages.',
    );
    expect(buttonNamed('Try again')).toBeUndefined();
  });

  it('starts over with New conversation', async () => {
    await openPanel();
    const newConversation = () =>
      element.querySelector<HTMLButtonElement>('button[title="New conversation"]')!;
    expect(newConversation().disabled).toBe(true);

    await type('Hi');
    newConversation().click();
    await settle();

    expect(element.querySelector('.from-user')).toBeNull();
    expect(element.querySelector('.suggestion')).not.toBeNull();
    await type('Hello again');
    expect(sent()).toEqual([{ role: 'user', text: 'Hello again' }]);
  });
});
