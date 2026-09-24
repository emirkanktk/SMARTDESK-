import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CurrentUser } from '../../core/models/auth.model';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { OWNER_ID, fakeReservation } from '../../../testing/reservation-fixtures';
import { DESK_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { ReservationForm } from './reservation-form';

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
  let getResources: ReturnType<typeof vi.fn>;
  let getReservation: ReturnType<typeof vi.fn>;
  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ReservationForm>;
  let element: HTMLElement;

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
    currentUser.set(owner);
    isAdmin.set(false);
    getResources = vi.fn(() => of([room, desk]));
    getReservation = vi.fn(() => of(fakeReservation()));
    create = vi.fn(() => of(fakeReservation()));
    update = vi.fn(() => of(fakeReservation()));
    success = vi.fn();

    await TestBed.configureTestingModule({
      imports: [ReservationForm],
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResources } },
        { provide: ReservationService, useValue: { getReservation, create, update } },
        { provide: AuthService, useValue: { currentUser, isAdmin } },
        { provide: NotificationService, useValue: { success } },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => vi.useRealTimers());

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
      });
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
});
