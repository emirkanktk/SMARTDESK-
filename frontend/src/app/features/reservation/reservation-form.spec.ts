import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CurrentUser } from '../../core/models/auth.model';
import { ApiError } from '../../core/models/api-error.model';
import { Invitee, SeriesDate, SeriesResult } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReminderAlertsService } from '../../core/services/reminder-alerts.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { DESK_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { RESOURCE_PHOTO_STUB } from '../../../testing/resource-photo-stub';
import { ATTENDEE_SEARCH_FROM, ReservationForm, SERIES_PREVIEW_DEBOUNCE } from './reservation-form';

// Local wall-clock time -> the UTC ISO string the API uses, whatever zone the tests run in.
const utc = (local: string) => new Date(local).toISOString();

describe('ReservationForm', () => {
  const NOW = new Date('2026-10-05T10:07'); // local time
  const room = fakeResource();
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    capacity: 1,
    location: '3rd Floor',
    features: [],
  });
  const owner: CurrentUser = {
    id: OWNER_ID,
    firstName: 'Ayşe',
    lastName: 'Yılmaz',
    email: 'ayse@smartdesk.local',
    roles: ['Employee'],
  };

  const currentUser = signal<CurrentUser | null>(owner);
  const isAdmin = signal(false);
  const isManager = signal(false);
  let getResources: ReturnType<typeof vi.fn>;
  let getInvitees: ReturnType<typeof vi.fn>;
  let getReservation: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let cancel: ReturnType<typeof vi.fn>;
  let previewSeries: ReturnType<typeof vi.fn>;
  let createSeries: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.spyOn>;
  let success: ReturnType<typeof vi.fn>;
  let reminderAlerts: {
    permission: ReturnType<typeof vi.fn>;
    requestPermissionIfUnasked: ReturnType<typeof vi.fn>;
  };
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ReservationForm>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    currentUser.set(owner);
    isAdmin.set(false);
    isManager.set(false);
    getResources = vi.fn(() => of([room, desk]));
    getInvitees = vi.fn(() => of([]));
    getReservation = vi.fn(() => of(fakeReservation()));
    create = vi.fn(() => of(fakeReservation()));
    update = vi.fn(() => of(fakeReservation()));
    cancel = vi.fn(() => of(fakeReservation({ status: 'Cancelled' })));
    previewSeries = vi.fn(() => of({ dates: [] }));
    createSeries = vi.fn();
    confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    success = vi.fn();
    reminderAlerts = {
      permission: vi.fn(() => 'default'),
      requestPermissionIfUnasked: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [ReservationForm],
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResources } },
        {
          provide: ReservationService,
          useValue: {
            getReservation,
            getInvitees,
            create,
            update,
            cancel,
            previewSeries,
            createSeries,
          },
        },
        // The dates of a series are asked for at once, rather than after a pause for typing.
        { provide: SERIES_PREVIEW_DEBOUNCE, useValue: 0 },
        { provide: AuthService, useValue: { currentUser, isAdmin, isManager } },
        { provide: NotificationService, useValue: { success } },
        { provide: ReminderAlertsService, useValue: reminderAlerts },
        RESOURCE_PHOTO_STUB,
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    confirmSpy.mockRestore();
  });

  async function render(inputs: Record<string, string> = {}): Promise<void> {
    fixture = TestBed.createComponent(ReservationForm);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function field<T extends HTMLElement = HTMLInputElement>(selector: string): T {
    return element.querySelector<T>(selector)!;
  }

  async function set(selector: string, value: string): Promise<void> {
    const el = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input'));
    el.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  /** A reservation that started 37 minutes ago, with seconds the picker can't show. */
  function inProgress() {
    return fakeReservation({
      startDateTime: new Date('2026-10-05T09:30:27').toISOString(),
      endDateTime: utc('2026-10-05T11:00'),
    });
  }

  describe('booking', () => {
    it('pre-fills the resource and times from the Book link', async () => {
      await render({
        resourceId: room.id,
        start: utc('2026-10-07T14:00'),
        end: utc('2026-10-07T15:30'),
      });

      expect(field<HTMLSelectElement>('#resource').value).toBe(room.id);
      expect(field('#start').value).toBe('2026-10-07T14:00');
      expect(field('#end').value).toBe('2026-10-07T15:30');
      expect(element.textContent).toContain('1 h 30 min');
      expect(element.querySelector('.resource-info')?.textContent).toContain('8 people');
      expect(getResources).toHaveBeenCalledWith({ status: 'Active' });
    });

    it("shows the chosen resource's picture next to its details", async () => {
      await render({ resourceId: room.id });

      expect(
        element.querySelector('.resource-preview .preview-photo img')!.getAttribute('src'),
      ).toBe(`type-picture:${room.resourceTypeName}`);
    });

    it('says when a resource must be checked in at the door, and only then', async () => {
      getResources.mockReturnValue(of([{ ...room, checkInRequired: true }, desk]));
      await render({ resourceId: room.id });

      expect(element.querySelector('.check-in-note')!.textContent).toContain(
        'scan the QR code at the door from 10 minutes before the start until 15 minutes after',
      );

      const select = field<HTMLSelectElement>('#resource');
      select.value = desk.id;
      select.dispatchEvent(new Event('change'));
      await fixture.whenStable();

      expect(element.querySelector('.check-in-note')).toBeNull();
    });

    it('starts at the next half hour for one hour without a link', async () => {
      await render();

      expect(field<HTMLSelectElement>('#resource').value).toBe('');
      expect(field('#start').value).toBe('2026-10-05T10:30');
      expect(field('#end').value).toBe('2026-10-05T11:30');
    });

    it('groups the resources by type', async () => {
      await render();

      const groups = Array.from(element.querySelectorAll('optgroup')).map((g) => g.label);
      expect(groups).toEqual(['Desk', 'Meeting Room']);
    });

    it('books with UTC times and a trimmed note, then goes to my reservations', async () => {
      await render({
        resourceId: room.id,
        start: utc('2026-10-07T14:00'),
        end: utc('2026-10-07T15:30'),
      });
      await set('#note', '  Sprint planning  ');
      await submit();

      expect(create).toHaveBeenCalledWith({
        resourceId: room.id,
        startDateTime: utc('2026-10-07T14:00'),
        endDateTime: utc('2026-10-07T15:30'),
        note: 'Sprint planning',
        reminderMinutes: 15,
        attendeeIds: [],
      });
      expect(success.mock.calls[0][0]).toContain('Meeting Room A is booked for');
      expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
    });

    it('sends no note when it is blank', async () => {
      await render({ resourceId: room.id });
      await set('#note', '   ');
      await submit();

      expect(create.mock.calls[0][0].note).toBeNull();
    });

    it('requires a resource', async () => {
      await render();
      await submit();

      expect(element.textContent).toContain('Choose a resource.');
      expect(create).not.toHaveBeenCalled();
    });

    it('accepts a start up to 5 minutes ago, like the API', async () => {
      await render({ resourceId: room.id });
      await set('#start', '2026-10-05T10:03');
      await submit();

      expect(create).toHaveBeenCalled();
    });

    it('rejects a start more than 5 minutes ago', async () => {
      await render({ resourceId: room.id });
      await set('#start', '2026-10-05T10:00');
      await submit();

      expect(element.textContent).toContain('The start time is in the past.');
      expect(create).not.toHaveBeenCalled();
    });

    it('rejects a start more than 180 days ahead, like the API', async () => {
      await render({ resourceId: room.id });
      await set('#start', '2027-04-05T10:00');
      await submit();

      expect(element.textContent).toContain('Bookings can start at most 180 days ahead.');
      expect(create).not.toHaveBeenCalled();
    });

    it('shows a conflict as the API words it, with a search for that time', async () => {
      create.mockReturnValue(
        throwError(
          () =>
            new ApiError(
              409,
              'Conflict',
              'Meeting Room A is already reserved for part of that time.',
            ),
        ),
      );
      await render({
        resourceId: room.id,
        start: utc('2026-10-07T14:00'),
        end: utc('2026-10-07T15:00'),
      });
      await submit();

      const alert = field<HTMLElement>('.conflict');
      expect(alert.textContent).toContain(
        'Meeting Room A is already reserved for part of that time.',
      );
      const link = new URL(alert.querySelector('a')!.href);
      expect(link.pathname).toBe('/availability');
      expect(link.searchParams.get('start')).toBe(utc('2026-10-07T14:00'));
      expect(link.searchParams.get('end')).toBe(utc('2026-10-07T15:00'));
      expect(link.searchParams.get('type')).toBe(room.resourceTypeId);
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('shows the API validation message next to the field', async () => {
      create.mockReturnValue(
        throwError(() => new ApiError(400, 'Invalid', null, { Note: ['The note is too long.'] })),
      );
      await render({ resourceId: room.id });
      await submit();

      expect(element.textContent).toContain('The note is too long.');
    });
    it('says so when the resources cannot be loaded', async () => {
      getResources.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render();

      expect(element.textContent).toContain('The list of resources could not be loaded.');
    });

    it('asks for a start and an end', async () => {
      await render({ resourceId: room.id });
      await set('#start', '');
      await set('#end', '');
      await submit();

      expect(element.textContent).toContain('Choose a start date and time.');
      expect(element.textContent).toContain('Choose an end date and time.');
      expect(create).not.toHaveBeenCalled();
    });

    it('checks the end is after the start, and at most 31 days later', async () => {
      await render({ resourceId: room.id });
      await set('#start', '2026-10-07T14:00');
      await set('#end', '2026-10-07T13:00');
      expect(element.textContent).toContain('The end must be after the start.');

      await set('#end', '2026-11-07T14:15'); // 31 days and a quarter of an hour
      expect(element.textContent).toContain('A reservation can last at most 31 days.');

      await submit();
      expect(create).not.toHaveBeenCalled();
    });

    it('shows at once what is wrong with an old Book link', async () => {
      await render({
        resourceId: room.id,
        start: utc('2026-10-05T08:00'),
        end: utc('2026-10-05T09:00'),
      });

      expect(element.textContent).toContain('The start time is in the past.');
    });

    it('puts the API messages next to resource, start and end, and the rest above the form', async () => {
      create.mockReturnValue(
        throwError(
          () =>
            new ApiError(400, 'Invalid', null, {
              ResourceId: ['That resource does not exist.'],
              StartDateTime: ['The start is too far ahead.'],
              EndDateTime: ['The end is too far ahead.'],
              Other: ['Something else is wrong.'],
            }),
        ),
      );
      await render({ resourceId: room.id });
      await submit();

      const fieldErrors = Array.from(element.querySelectorAll('.field-error')).map((e) =>
        e.textContent!.trim(),
      );
      expect(fieldErrors).toEqual([
        'That resource does not exist.',
        'The start is too far ahead.',
        'The end is too far ahead.',
      ]);
      expect(field<HTMLElement>('.alert-error').textContent!.trim()).toBe(
        'Something else is wrong.',
      );
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('keeps a note on the form when saving fails otherwise', async () => {
      create.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render({ resourceId: room.id });
      await submit();

      expect(element.textContent).toContain('The reservation could not be saved.');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('names both days in the toast when the booking spans them', async () => {
      create.mockReturnValue(
        of(
          fakeReservation({
            startDateTime: utc('2026-10-07T22:00'),
            endDateTime: utc('2026-10-08T02:00'),
          }),
        ),
      );
      await render({ resourceId: room.id });
      await submit();

      expect(success).toHaveBeenCalledWith(
        'Meeting Room A is booked for Wed 7 Oct, 22:00 to Thu 8 Oct, 02:00.',
      );
    });
  });

  describe('changing a reservation', () => {
    it('loads an upcoming reservation and saves it with PUT', async () => {
      // Times set in local time, so the new end below is after the start in every zone.
      getReservation.mockReturnValue(
        of(
          fakeReservation({
            startDateTime: utc('2026-10-07T14:00'),
            endDateTime: utc('2026-10-07T15:30'),
          }),
        ),
      );
      await render({ id: 'rsv-1' });

      expect(getReservation).toHaveBeenCalledWith('rsv-1');
      expect(field<HTMLSelectElement>('#resource').value).toBe('res-1');
      expect(field<HTMLTextAreaElement>('#note').value).toBe('Sprint planning');

      await set('#end', '2026-10-07T17:00');
      await set('#note', 'Retro');
      await submit();

      expect(update).toHaveBeenCalledWith('rsv-1', {
        resourceId: 'res-1',
        startDateTime: utc('2026-10-07T14:00'),
        endDateTime: utc('2026-10-07T17:00'),
        note: 'Retro',
        reminderMinutes: null,
        attendeeIds: [],
      });
      expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
    });

    it('lets only the end and note change once it has started, and resends the start as-is', async () => {
      const reservation = inProgress();
      getReservation.mockReturnValue(of(reservation));
      await render({ id: 'rsv-1' });

      expect(element.textContent).toContain('only its end time and note can change');
      expect(field<HTMLSelectElement>('#resource').disabled).toBe(true);
      expect(field('#start').disabled).toBe(true);

      await set('#end', '2026-10-05T11:30');
      await submit();

      expect(update).toHaveBeenCalledWith('rsv-1', {
        resourceId: 'res-1',
        startDateTime: reservation.startDateTime, // seconds kept, so the API sees no change
        endDateTime: utc('2026-10-05T11:30'),
        note: 'Sprint planning',
        reminderMinutes: null,
        attendeeIds: [],
      });
    });

    it('still saves a note when the reservation started while the form was open', async () => {
      getReservation.mockReturnValue(
        of(
          fakeReservation({
            startDateTime: utc('2026-10-05T10:15'),
            endDateTime: utc('2026-10-05T11:15'),
          }),
        ),
      );
      await render({ id: 'rsv-1' }); // upcoming at 10:07
      vi.setSystemTime(new Date('2026-10-05T10:30')); // started 15 minutes ago
      await set('#note', 'Moved to the big screen');
      await submit();

      expect(update).toHaveBeenCalledWith(
        'rsv-1',
        expect.objectContaining({
          startDateTime: utc('2026-10-05T10:15'),
          note: 'Moved to the big screen',
        }),
      );
    });

    it('rejects moving the end of a started reservation into the past', async () => {
      getReservation.mockReturnValue(of(inProgress()));
      await render({ id: 'rsv-1' });
      await set('#end', '2026-10-05T10:00');
      await submit();

      expect(element.textContent).toContain('The end time has already passed.');
      expect(update).not.toHaveBeenCalled();
    });

    it.each([
      [
        'a cancelled reservation',
        fakeReservation({ status: 'Cancelled' }),
        'was cancelled, so it can no longer be changed',
      ],
      [
        'one that has ended',
        fakeReservation({
          startDateTime: utc('2026-10-05T08:00'),
          endDateTime: utc('2026-10-05T09:00'),
        }),
        'has ended, so it can no longer be changed',
      ],
      [
        "someone else's",
        fakeReservation({ userId: 'someone-else', userDisplayName: 'Mehmet Demir' }),
        'Only Mehmet Demir or an admin can change this reservation.',
      ],
    ])('shows %s read-only', async (_, reservation, reason) => {
      getReservation.mockReturnValue(of(reservation));
      await render({ id: 'rsv-1' });

      expect(element.textContent).toContain(reason);
      expect(element.querySelector('form')).toBeNull();
      expect(element.textContent).toContain('Meeting Room A');
    });

    it('copes with an owner the API can no longer name', async () => {
      getReservation.mockReturnValue(
        of(fakeReservation({ userId: 'someone-else', userDisplayName: null })),
      );
      await render({ id: 'rsv-1' });

      expect(element.textContent).toContain(
        'Only the person who booked it or an admin can change this reservation.',
      );
      expect(element.textContent).toContain('Unknown user');
      expect(element.textContent).not.toContain('null');
    });

    it("lets an admin change someone else's reservation and says whose it is", async () => {
      isAdmin.set(true);
      getReservation.mockReturnValue(
        of(fakeReservation({ userId: 'someone-else', userDisplayName: 'Mehmet Demir' })),
      );
      await render({ id: 'rsv-1' });

      expect(element.querySelector('form')).not.toBeNull();
      expect(element.textContent).toContain('Booked by Mehmet Demir');
    });

    it('keeps its resource selectable after it stopped being bookable', async () => {
      getResources.mockReturnValue(of([desk])); // Meeting Room A went into maintenance
      await render({ id: 'rsv-1' });

      const select = field<HTMLSelectElement>('#resource');
      expect(select.value).toBe('res-1');
      expect(select.selectedOptions[0].textContent).toContain('not bookable right now');
    });

    it('says so when the reservation cannot be loaded', async () => {
      getReservation.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
      await render({ id: 'missing' });

      expect(element.textContent).toContain('This reservation could not be loaded.');
    });
  });

  describe('cancelling', () => {
    // Local times, so the wording is the same in every zone.
    const upcoming = fakeReservation({
      startDateTime: utc('2026-10-07T14:00'),
      endDateTime: utc('2026-10-07T15:30'),
    });

    function cancelButton(): HTMLButtonElement | null {
      return element.querySelector<HTMLButtonElement>('.cancel-reservation');
    }

    it('cancels your own reservation after confirming, then goes to my reservations', async () => {
      getReservation.mockReturnValue(of(upcoming));
      await render({ id: 'rsv-1' });
      cancelButton()!.click();
      await fixture.whenStable();

      expect(confirmSpy).toHaveBeenCalledWith(
        'Cancel your Meeting Room A reservation for Wed 7 Oct, 14:00-15:30?',
      );
      expect(cancel).toHaveBeenCalledWith('rsv-1');
      expect(success).toHaveBeenCalledWith(
        'Your Meeting Room A reservation for Wed 7 Oct, 14:00-15:30 was cancelled.',
      );
      expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
    });

    it("lets an admin cancel someone else's, naming them, then goes back to the admin panel", async () => {
      isAdmin.set(true);
      getReservation.mockReturnValue(
        of({ ...upcoming, userId: 'someone-else', userDisplayName: 'Mehmet Demir' }),
      );
      await render({ id: 'rsv-1' });
      cancelButton()!.click();
      await fixture.whenStable();

      expect(confirmSpy).toHaveBeenCalledWith(
        "Cancel Mehmet Demir's Meeting Room A reservation for Wed 7 Oct, 14:00-15:30?",
      );
      expect(success).toHaveBeenCalledWith(
        "Mehmet Demir's Meeting Room A reservation for Wed 7 Oct, 14:00-15:30 was cancelled.",
      );
      expect(navigateByUrl).toHaveBeenCalledWith('/admin');
    });

    it('is offered for a reservation in progress', async () => {
      getReservation.mockReturnValue(of(inProgress()));
      await render({ id: 'rsv-1' });

      expect(cancelButton()).not.toBeNull();
    });

    it('is not offered when booking or for a read-only reservation', async () => {
      await render();
      expect(cancelButton()).toBeNull();

      getReservation.mockReturnValue(of(fakeReservation({ status: 'Cancelled' })));
      await render({ id: 'rsv-1' });
      expect(cancelButton()).toBeNull();
    });

    it('does nothing when the confirmation is declined', async () => {
      confirmSpy.mockReturnValue(false);
      getReservation.mockReturnValue(of(upcoming));
      await render({ id: 'rsv-1' });
      cancelButton()!.click();
      await fixture.whenStable();

      expect(cancel).not.toHaveBeenCalled();
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('shows it read-only when it ended in the meantime', async () => {
      getReservation.mockReturnValueOnce(of(upcoming)).mockReturnValue(
        of({
          ...upcoming,
          startDateTime: utc('2026-10-05T08:00'),
          endDateTime: utc('2026-10-05T09:00'),
        }),
      );
      cancel.mockReturnValue(
        throwError(() => new ApiError(409, 'Conflict', 'This reservation has already ended.')),
      );
      await render({ id: 'rsv-1' });
      cancelButton()!.click();
      await fixture.whenStable();

      expect(getReservation).toHaveBeenCalledTimes(2);
      expect(element.textContent).toContain('has ended, so it can no longer be changed');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('keeps the form with a note when cancelling fails otherwise', async () => {
      getReservation.mockReturnValue(of(upcoming));
      cancel.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render({ id: 'rsv-1' });
      cancelButton()!.click();
      await fixture.whenStable();

      expect(element.textContent).toContain('The reservation could not be cancelled.');
      expect(cancelButton()!.disabled).toBe(false);
    });
  });

  describe('inviting people (ADR-0032)', () => {
    const mehmet: Invitee = {
      id: 'u-mehmet',
      name: 'Mehmet Demir',
      email: 'mehmet@company.example',
    };
    const can: Invitee = { id: 'u-can', name: 'Can Öztürk', email: 'can@company.example' };

    function people(): string[] {
      return Array.from(element.querySelectorAll('.person-name')).map((n) => n.textContent!.trim());
    }

    function ticked(): string[] {
      return Array.from(element.querySelectorAll<HTMLLabelElement>('.person'))
        .filter((label) => label.querySelector('input')!.checked)
        .map((label) => label.querySelector('.person-name')!.textContent!.trim());
    }

    async function tick(name: string): Promise<void> {
      Array.from(element.querySelectorAll<HTMLLabelElement>('.person'))
        .find((label) => label.textContent!.includes(name))!
        .querySelector('input')!
        .click();
      await fixture.whenStable();
    }

    function attendeeHint(): string {
      return element
        .querySelector('.attendees .hint, .attendees .field-error')!
        .textContent!.trim();
    }

    it('offers no list to an employee, and asks nobody for one', async () => {
      await render({ resourceId: room.id });

      expect(getInvitees).not.toHaveBeenCalled();
      expect(element.querySelector('.attendees')).toBeNull();
    });

    it('lets a manager tick people from the team, and sends them with the booking', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet, can]));
      await render({ resourceId: room.id });

      expect(people()).toEqual(['Can Öztürk', 'Mehmet Demir']);
      expect(element.querySelector('.person-email')!.textContent).toBe('can@company.example');
      expect(attendeeHint()).toContain(
        'Each person you invite gets a message in their SmartDesk inbox',
      );

      await tick('Mehmet Demir');
      expect(attendeeHint()).toMatch(/^1 invited\./);
      await submit();

      expect(create).toHaveBeenCalledWith(expect.objectContaining({ attendeeIds: ['u-mehmet'] }));
      expect(success.mock.calls[0][0]).toMatch(/is booked for .*\. Your invitations were sent\.$/);
    });

    it('lets a tick be taken back', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet, can]));
      await render({ resourceId: room.id });
      await tick('Mehmet Demir');
      await tick('Can Öztürk');
      await tick('Mehmet Demir');
      await submit();

      expect(create).toHaveBeenCalledWith(expect.objectContaining({ attendeeIds: ['u-can'] }));
    });

    it('says when the resource has no room for everyone, the organizer counted', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet]));
      await render({ resourceId: desk.id });
      await tick('Mehmet Demir');

      expect(attendeeHint()).toBe('Desk 12 has room for 1, and with you the meeting is 2.');
      await submit();
      expect(create).not.toHaveBeenCalled();

      await set('#resource', room.id);
      expect(attendeeHint()).toMatch(/^1 invited\./);
    });

    it("shows the API's reason next to the list", async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet]));
      create.mockReturnValue(
        throwError(
          () =>
            new ApiError(400, 'Invalid', null, {
              AttendeeIds: ['Mehmet Demir is no longer in your team.'],
            }),
        ),
      );
      await render({ resourceId: room.id });
      await tick('Mehmet Demir');
      await submit();

      expect(element.querySelector('.attendees .field-error')!.textContent).toBe(
        'Mehmet Demir is no longer in your team.',
      );
    });

    it('keeps who is invited when changing a booking, and lets someone who left the team go', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet]));
      getReservation.mockReturnValue(
        of(
          fakeReservation({
            attendees: [
              { id: 'u-mehmet', name: 'Mehmet Demir', response: 'Pending' },
              { id: 'u-left', name: 'Burcu Aydın', response: 'Pending' },
            ],
          }),
        ),
      );
      await render({ id: 'rsv-1' });

      expect(people()).toEqual(['Burcu Aydın', 'Mehmet Demir']);
      expect(ticked()).toEqual(['Burcu Aydın', 'Mehmet Demir']);

      await tick('Burcu Aydın');
      await submit();

      expect(update).toHaveBeenCalledWith(
        'rsv-1',
        expect.objectContaining({ attendeeIds: ['u-mehmet'] }),
      );
      // Nobody new: nothing was sent.
      expect(success.mock.calls[0][0]).not.toContain('invitations');
    });

    it("sends the list back as it is when an employee changes a booking someone's invited to", async () => {
      getReservation.mockReturnValue(
        of(
          fakeReservation({
            attendees: [{ id: 'u-mehmet', name: 'Mehmet Demir', response: 'Pending' }],
          }),
        ),
      );
      await render({ id: 'rsv-1' });
      await set('#note', 'Retro');
      await submit();

      expect(getInvitees).not.toHaveBeenCalled();
      expect(update).toHaveBeenCalledWith(
        'rsv-1',
        expect.objectContaining({ attendeeIds: ['u-mehmet'] }),
      );
    });

    it("leaves the booking's owner off the list when an admin changes it", async () => {
      isAdmin.set(true);
      getInvitees.mockReturnValue(of([mehmet, can]));
      getReservation.mockReturnValue(
        of(fakeReservation({ userId: 'u-mehmet', userDisplayName: 'Mehmet Demir' })),
      );
      await render({ id: 'rsv-1' });

      expect(people()).toEqual(['Can Öztürk']);
    });

    it('searches by name or email once the list is long', async () => {
      isAdmin.set(true);
      const many = Array.from({ length: ATTENDEE_SEARCH_FROM - 1 }, (_, i) => ({
        id: `u-${i}`,
        name: `Person ${i}`,
        email: `person${i}@company.example`,
      }));
      getInvitees.mockReturnValue(of([...many, mehmet]));
      await render({ resourceId: room.id });
      const search = field('.attendee-search');

      search.value = 'MEHMET@';
      search.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(people()).toEqual(['Mehmet Demir']);

      search.value = 'nobody';
      search.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(element.querySelector('.no-match')!.textContent).toContain('Nobody matches');
    });

    it('has no search box for a short list', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(of([mehmet, can]));
      await render({ resourceId: room.id });

      expect(element.querySelector('.attendee-search')).toBeNull();
    });

    it('says so when the list of people could not be loaded', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render({ resourceId: room.id });

      expect(element.textContent).toContain('The list of people to invite could not be loaded.');
    });

    it('tells someone invited who organized the meeting, and who else is coming', async () => {
      currentUser.set({ ...owner, id: 'u-mehmet' });
      getReservation.mockReturnValue(
        of(
          fakeReservation({
            startDateTime: utc('2026-10-07T14:00'),
            endDateTime: utc('2026-10-07T15:30'),
            userDisplayName: 'Zeynep Kaya',
            attendees: [
              { id: 'u-mehmet', name: 'Mehmet Demir', response: 'Pending' },
              { id: 'u-gone', name: null, response: 'Pending' },
            ],
          }),
        ),
      );
      await render({ id: 'rsv-1' });

      expect(element.querySelector('.notice')!.textContent).toContain(
        'Zeynep Kaya invited you to this meeting. Only they or an admin can change it.',
      );
      expect(element.querySelector('.attendee-names')!.textContent).toBe(
        'Mehmet Demir (no answer yet), Unknown user (no answer yet)',
      );
      expect(element.querySelector('app-meeting-answer')!.textContent).toContain('Will you come?');
    });
  });

  describe('answers to the invitation (ADR-0032)', () => {
    const answered = (response: 'Accepted' | 'Declined' | 'Pending') =>
      fakeReservation({
        startDateTime: utc('2026-10-07T14:00'),
        endDateTime: utc('2026-10-07T15:30'),
        userDisplayName: 'Zeynep Kaya',
        attendees: [
          { id: 'u-mehmet', name: 'Mehmet Demir', response },
          { id: 'u-burcu', name: 'Burcu Aydın', response: 'Declined' },
        ],
      });

    it('shows the organizer what each person on the list answered', async () => {
      isManager.set(true);
      getInvitees.mockReturnValue(
        of([
          { id: 'u-mehmet', name: 'Mehmet Demir', email: 'mehmet@company.example' },
          { id: 'u-can', name: 'Can Öztürk', email: 'can@company.example' },
        ]),
      );
      getReservation.mockReturnValue(of(answered('Accepted')));
      await render({ id: 'rsv-1' });

      const tags = Array.from(element.querySelectorAll('.person')).map((person) => [
        person.querySelector('.person-name')!.textContent!.trim(),
        person.querySelector('.response')?.textContent?.trim() ?? null,
      ]);
      expect(tags).toEqual([
        ['Burcu Aydın', "can't come"],
        ['Can Öztürk', null], // not invited yet
        ['Mehmet Demir', 'coming'],
      ]);
    });

    it('lets someone invited answer from the meeting', async () => {
      currentUser.set({ ...owner, id: 'u-mehmet' });
      getReservation.mockReturnValue(of(answered('Pending')));
      const respond = vi.fn(() => of(answered('Accepted')));
      TestBed.inject(ReservationService).respond = respond;
      await render({ id: 'rsv-1' });

      Array.from(element.querySelectorAll<HTMLButtonElement>('app-meeting-answer button'))
        .find((b) => b.textContent!.trim() === "I'm coming")!
        .click();
      await fixture.whenStable();

      expect(respond).toHaveBeenCalledWith('rsv-1', { response: 'Accepted' });
      expect(element.querySelector('.attendee-names')!.textContent).toContain(
        'Mehmet Demir (coming)',
      );
    });

    it('asks nothing once the meeting is over or cancelled', async () => {
      currentUser.set({ ...owner, id: 'u-mehmet' });
      getReservation.mockReturnValue(of({ ...answered('Pending'), status: 'Cancelled' as const }));
      await render({ id: 'rsv-1' });
      expect(element.querySelector('app-meeting-answer')).toBeNull();

      getReservation.mockReturnValue(
        of({
          ...answered('Pending'),
          startDateTime: utc('2026-10-05T08:00'),
          endDateTime: utc('2026-10-05T09:00'),
        }),
      );
      await render({ id: 'rsv-1' });
      expect(element.querySelector('app-meeting-answer')).toBeNull();
    });
  });

  describe('reminder', () => {
    it('starts a new booking with a 15-minute reminder and offers the other choices', async () => {
      await render({ resourceId: room.id });

      const options = Array.from(field<HTMLSelectElement>('#reminder').options).map((o) =>
        o.textContent!.trim(),
      );
      expect(options).toEqual([
        'No reminder',
        '5 minutes before',
        '15 minutes before',
        '30 minutes before',
        '60 minutes before',
      ]);
      expect(field<HTMLSelectElement>('#reminder').selectedOptions[0].textContent!.trim()).toBe(
        '15 minutes before',
      );
    });

    it('books with the chosen reminder, or none, and asks for notifications only with one', async () => {
      await render({ resourceId: room.id });
      await set('#reminder', field<HTMLSelectElement>('#reminder').options[3].value); // 30
      await submit();
      expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ reminderMinutes: 30 }));
      expect(reminderAlerts.requestPermissionIfUnasked).toHaveBeenCalledTimes(1);

      await render({ resourceId: room.id });
      await set('#reminder', field<HTMLSelectElement>('#reminder').options[0].value); // none
      await submit();
      expect(create).toHaveBeenLastCalledWith(expect.objectContaining({ reminderMinutes: null }));
      expect(reminderAlerts.requestPermissionIfUnasked).toHaveBeenCalledTimes(1);
    });

    it('shows and keeps the stored reminder when changing a reservation', async () => {
      getReservation.mockReturnValue(of(fakeReservation({ reminderMinutes: 60 })));
      await render({ id: 'rsv-1' });

      expect(field<HTMLSelectElement>('#reminder').selectedOptions[0].textContent!.trim()).toBe(
        '60 minutes before',
      );
      await set('#note', 'Retro');
      await submit();
      expect(update).toHaveBeenCalledWith(
        'rsv-1',
        expect.objectContaining({ note: 'Retro', reminderMinutes: 60 }),
      );
    });

    it('hides the reminder once the reservation has started, and sends the stored one back', async () => {
      getReservation.mockReturnValue(of({ ...inProgress(), reminderMinutes: 5 }));
      await render({ id: 'rsv-1' });

      expect(element.querySelector('#reminder')).toBeNull();
      await set('#end', '2026-10-05T11:30');
      await submit();
      expect(update).toHaveBeenCalledWith('rsv-1', expect.objectContaining({ reminderMinutes: 5 }));
    });

    it('says the reminder stays in the app when desktop notifications are blocked', async () => {
      reminderAlerts.permission.mockReturnValue('denied');
      await render({ resourceId: room.id });

      expect(element.textContent).toContain('Desktop notifications are blocked in this browser');
    });
  });
  describe('repeating (ADR-0035)', () => {
    // Mondays at 10:30-11:30, local time: the form's start without a link.
    const monday = (day: number) => ({
      startDateTime: utc(`2026-10-${String(day).padStart(2, '0')}T10:30`),
      endDateTime: utc(`2026-10-${String(day).padStart(2, '0')}T11:30`),
    });
    const free = (day: number): SeriesDate => ({
      ...monday(day),
      free: true,
      code: null,
      reason: null,
    });
    const taken = (day: number): SeriesDate => ({
      ...monday(day),
      free: false,
      code: 'reservation-overlaps',
      reason: 'Meeting Room A is already reserved for part of that time.',
    });
    const series = {
      id: 'series-1',
      frequency: 'Weekly' as const,
      interval: 1,
      daysOfWeek: ['Monday' as const],
      until: '2026-11-02',
      timeZone: 'Europe/Istanbul',
    };
    const result = (booked: number[], skipped: SeriesDate[] = []): SeriesResult => ({
      series,
      booked: booked.map((day) => fakeReservation({ id: `rsv-${day}`, ...monday(day), series })),
      skipped,
    });

    /** The dates are asked for after a pause, here none: let it pass. */
    async function settle(): Promise<void> {
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      await fixture.whenStable();
    }

    async function repeat(choice: string): Promise<void> {
      await set('.repeat select', choice);
      await settle();
    }

    const submitButton = () =>
      element.querySelector<HTMLButtonElement>('button[type=submit]')!.textContent!.trim();

    it('is offered for a new booking only', async () => {
      await render({ resourceId: room.id });
      expect(element.querySelector('.repeat select')).not.toBeNull();
      expect(field<HTMLSelectElement>('.repeat select').value).toBe('none');

      fixture.destroy();
      await render({ id: 'rsv-1' });
      expect(element.querySelector('.repeat')).toBeNull();
    });

    it('repeats weekly on the day it starts, four weeks on, and shows the dates before booking', async () => {
      previewSeries.mockReturnValue(
        of({ dates: [free(5), free(12), free(19), free(26), free(2)] }),
      );
      await render({ resourceId: room.id });

      await repeat('weekly');

      expect(field('#until').value).toBe('2026-11-02');
      expect(previewSeries).toHaveBeenLastCalledWith({
        resourceId: room.id,
        startDateTime: utc('2026-10-05T10:30'),
        endDateTime: utc('2026-10-05T11:30'),
        recurrence: {
          frequency: 'Weekly',
          interval: 1,
          daysOfWeek: ['Monday'],
          until: '2026-11-02',
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        },
      });
      const days = Array.from(element.querySelectorAll<HTMLInputElement>('.days input'));
      expect(days.map((d) => d.checked)).toEqual([true, false, false, false, false, false, false]);
      expect(days[0].disabled).toBe(true);
      expect(element.querySelector('app-series-dates')!.textContent).toContain(
        '5 dates, all free.',
      );
      expect(submitButton()).toBe('Book 5 dates');
    });

    it('adds days and repeats every other week', async () => {
      await render({ resourceId: room.id });
      await repeat('biweekly');

      const thursday = element.querySelectorAll<HTMLInputElement>('.days input')[3];
      thursday.checked = true;
      thursday.dispatchEvent(new Event('change'));
      await settle();

      expect(previewSeries).toHaveBeenLastCalledWith(
        expect.objectContaining({
          recurrence: expect.objectContaining({ interval: 2, daysOfWeek: ['Monday', 'Thursday'] }),
        }),
      );
    });

    it('repeats daily, or on weekdays, with no days to pick', async () => {
      await render({ resourceId: room.id });

      await repeat('daily');
      expect(element.querySelector('.days')).toBeNull();
      expect(previewSeries).toHaveBeenLastCalledWith(
        expect.objectContaining({
          recurrence: expect.objectContaining({ frequency: 'Daily', interval: 1, daysOfWeek: [] }),
        }),
      );

      await repeat('weekdays');
      expect(element.querySelector('.days')).toBeNull();
      expect(previewSeries).toHaveBeenLastCalledWith(
        expect.objectContaining({
          recurrence: expect.objectContaining({
            frequency: 'Weekly',
            daysOfWeek: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
          }),
        }),
      );
    });

    it('books every date when all are free', async () => {
      previewSeries.mockReturnValue(of({ dates: [free(5), free(12)] }));
      createSeries.mockReturnValue(of(result([5, 12])));
      await render({ resourceId: room.id });
      await repeat('weekly');

      await submit();

      expect(createSeries).toHaveBeenCalledWith(
        expect.objectContaining({
          resourceId: room.id,
          startDateTime: utc('2026-10-05T10:30'),
          endDateTime: utc('2026-10-05T11:30'),
          reminderMinutes: 15,
          attendeeIds: [],
          recurrence: expect.objectContaining({ frequency: 'Weekly', until: '2026-11-02' }),
          skipConflicts: false,
        }),
      );
      expect(create).not.toHaveBeenCalled();
      expect(success).toHaveBeenCalledWith(
        'Meeting Room A is booked on 2 dates, every Monday until 2 Nov 2026, 10:30-11:30.',
      );
      expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
    });

    it("shows the dates that aren't free and books the others", async () => {
      previewSeries.mockReturnValue(of({ dates: [free(5), taken(12), free(19)] }));
      createSeries.mockReturnValue(of(result([5, 19], [taken(12)])));
      await render({ resourceId: room.id });
      await repeat('weekly');

      expect(element.querySelectorAll('app-series-dates li.taken')).toHaveLength(1);
      expect(submitButton()).toBe('Book the 2 free dates');

      await submit();

      expect(createSeries).toHaveBeenCalledWith(expect.objectContaining({ skipConflicts: true }));
      expect(success).toHaveBeenCalledWith(
        "Meeting Room A is booked on 2 dates, every Monday until 2 Nov 2026, 10:30-11:30. One date wasn't free and was skipped.",
      );
    });

    it('says the free date in the singular, and with none free offers nothing to book', async () => {
      previewSeries.mockReturnValue(of({ dates: [free(5), taken(12)] }));
      await render({ resourceId: room.id });
      await repeat('weekly');
      expect(submitButton()).toBe('Book the free date');

      previewSeries.mockReturnValue(of({ dates: [taken(5), taken(12)] }));
      await set('#until', '2026-10-12');
      await settle();
      expect(element.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
    });

    it('shows the dates again when one was taken while booking', async () => {
      previewSeries.mockReturnValue(of({ dates: [free(5), free(12)] }));
      createSeries.mockReturnValue(
        throwError(
          () =>
            new ApiError(
              409,
              'Conflict',
              "1 of the 2 dates aren't free in Meeting Room A. Look at the dates again, then book the others or choose another time.",
              {},
              'series-conflicts',
            ),
        ),
      );
      await render({ resourceId: room.id });
      await repeat('weekly');
      const asked = previewSeries.mock.calls.length;

      previewSeries.mockReturnValue(of({ dates: [free(5), taken(12)] }));
      await submit();
      await settle();

      expect(element.querySelector('.conflict')!.textContent).toContain(
        "1 of the 2 dates aren't free",
      );
      expect(previewSeries.mock.calls.length).toBe(asked + 1);
      expect(submitButton()).toBe('Book the free date');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('says what the API found wrong with the dates', async () => {
      previewSeries.mockReturnValue(
        throwError(
          () =>
            new ApiError(400, 'Invalid', null, {
              'Recurrence.Until': [
                "That's only one date: pick a later end, or more days, for it to repeat.",
              ],
            }),
        ),
      );
      await render({ resourceId: room.id });
      await repeat('weekly');

      expect(element.querySelector('.repeat .field-error')!.textContent).toContain(
        "That's only one date",
      );
    });

    it('says so when the dates could not be checked, and still lets the booker book', async () => {
      previewSeries.mockReturnValue(throwError(() => new ApiError(0, 'Unreachable')));
      await render({ resourceId: room.id });
      await repeat('weekly');

      expect(element.querySelector('.repeat')!.textContent).toContain(
        'The dates could not be checked right now.',
      );
      expect(element.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
    });

    it('checks the last day itself before asking', async () => {
      await render({ resourceId: room.id });
      await repeat('weekly');
      previewSeries.mockClear();

      await set('#until', '2027-05-01');
      await settle();
      expect(element.querySelector('.repeat .field-error')!.textContent).toContain(
        'A booking can repeat at most 180 days ahead.',
      );

      await set('#until', '2026-10-04');
      await settle();
      expect(element.querySelector('.repeat .field-error')!.textContent).toContain(
        "The last day can't be before the first.",
      );
      expect(previewSeries).not.toHaveBeenCalled();

      await submit();
      expect(createSeries).not.toHaveBeenCalled();
    });

    it('starts a series on weekdays on a weekday', async () => {
      await render({
        resourceId: room.id,
        start: utc('2026-10-10T10:00'),
        end: utc('2026-10-10T11:00'),
      });

      await repeat('weekdays');

      expect(element.querySelector('.repeat .field-error')!.textContent).toContain(
        'Start on a weekday for a booking that repeats on weekdays.',
      );
      expect(previewSeries).not.toHaveBeenCalled();
    });

    it('says a date of a series repeats, and that a change is for that date alone', async () => {
      getReservation.mockReturnValue(of(fakeReservation({ series })));
      await render({ id: 'rsv-1' });

      expect(element.querySelector('.series-note')!.textContent!.replace(/\s+/g, ' ')).toContain(
        'This is one date of a booking that repeats every Monday until 2 Nov 2026. Changes here are for this date only.',
      );
    });

    it("says how someone else's booking repeats", async () => {
      getReservation.mockReturnValue(of(fakeReservation({ userId: 'someone-else', series })));
      await render({ id: 'rsv-1' });

      const terms = Array.from(element.querySelectorAll('.details dt'));
      const repeats = terms.find((dt) => dt.textContent!.trim() === 'Repeats')!;
      expect(repeats.nextElementSibling!.textContent!.trim()).toBe('every Monday until 2 Nov 2026');
    });
  });
});
