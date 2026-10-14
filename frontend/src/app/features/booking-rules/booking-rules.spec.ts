import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  OfficeHoursRequest,
  ResourceTypeRulesRequest,
} from '../../core/models/booking-rules.model';
import { BookingRulesService } from '../../core/services/booking-rules.service';
import { NotificationService } from '../../core/services/notification.service';
import {
  BROWSER_ZONE,
  fakeBookingRules,
  fakeKindRules,
} from '../../../testing/booking-rules-fixtures';
import { BookingRulesAdmin } from './booking-rules';

describe('BookingRulesAdmin', () => {
  const desks = fakeKindRules({
    maxDurationMinutes: 540,
    maxDaysAhead: 7,
    maxActivePerUser: 2,
    officeHoursOnly: true,
  });
  const cars = fakeKindRules({ resourceTypeId: 'type-car', name: 'Company Car' });
  const rooms = fakeKindRules({ resourceTypeId: 'type-room', name: 'Meeting Room' });

  let get: ReturnType<typeof vi.fn>;
  let setOfficeHours: ReturnType<typeof vi.fn>;
  let setTypeRules: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<BookingRulesAdmin>;
  let element: HTMLElement;

  beforeEach(async () => {
    get = vi.fn(() => of(fakeBookingRules([rooms, desks, cars])));
    setOfficeHours = vi.fn((request: OfficeHoursRequest) =>
      of({ ...request, description: 'Mon-Sat 08:30-18:00' }),
    );
    setTypeRules = vi.fn((id: string, request: ResourceTypeRulesRequest) =>
      of({
        resourceTypeId: id,
        name: id === desks.resourceTypeId ? 'Desk' : 'Company Car',
        ...request,
      }),
    );
    success = vi.fn();
    await TestBed.configureTestingModule({
      imports: [BookingRulesAdmin],
      providers: [
        { provide: BookingRulesService, useValue: { get, setOfficeHours, setTypeRules } },
        { provide: NotificationService, useValue: { success } },
      ],
    }).compileComponents();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(BookingRulesAdmin);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const input = (selector: string) => element.querySelector<HTMLInputElement>(selector)!;
  const days = () =>
    Array.from(element.querySelectorAll<HTMLInputElement>('.workdays input[type=checkbox]'));
  const card = (heading: string) =>
    Array.from(element.querySelectorAll('.kind')).find(
      (kind) => kind.querySelector('h3')!.textContent!.trim() === heading,
    )!;

  async function type(selector: string, value: string): Promise<void> {
    const field = input(selector);
    field.value = value;
    field.dispatchEvent(new Event('input'));
    field.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submit(form: Element): Promise<void> {
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const officeForm = () => element.querySelector('form.office')!;

  it("shows the office's hours and each kind's own rules, by name", async () => {
    await render();

    expect(input('#opens').value).toBe('09:00');
    expect(input('#closes').value).toBe('18:00');
    expect(element.querySelector<HTMLSelectElement>('#zone')!.value).toBe(BROWSER_ZONE);
    expect(days().map((day) => day.checked)).toEqual([true, true, true, true, true, false, false]);
    expect(
      Array.from(element.querySelectorAll('.kind h3')).map((h) => h.textContent!.trim()),
    ).toEqual(['Company Car', 'Desk', 'Meeting Room']);
    expect(input('#hours-type-desk').value).toBe('9');
    expect(input('#ahead-type-desk').value).toBe('7');
    expect(input('#per-person-type-desk').value).toBe('2');
    expect(card('Desk').querySelector<HTMLInputElement>('input[type=checkbox]')!.checked).toBe(
      true,
    );
    expect(input('#hours-type-car').value).toBe('');
    expect(element.querySelector('.kinds-section .hint')!.textContent).toContain(
      'Any booking can last at most 31 days and start at most 180 days ahead.',
    );
  });

  it("saves the office's hours, its days in the week's order, and says them back", async () => {
    await render();
    await type('#opens', '08:30');
    days()[5].click();
    await fixture.whenStable();
    await submit(officeForm());

    expect(setOfficeHours).toHaveBeenCalledWith({
      timeZone: BROWSER_ZONE,
      opensAt: '08:30',
      closesAt: '18:00',
      workdays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
    });
    expect(success).toHaveBeenCalledWith('Office hours saved: Mon-Sat 08:30-18:00.');
  });

  it('keeps the hours on the quarter hour, closing after opening, on some day, before asking', async () => {
    await render();

    await type('#opens', '08:10');
    await submit(officeForm());
    expect(element.querySelector('#opens-error')!.textContent).toContain(
      'Use a time on the quarter hour, such as 08:00, 08:15 or 08:30.',
    );

    await type('#opens', '09:00');
    await type('#closes', '08:00');
    await submit(officeForm());
    expect(element.querySelector('#closes-error')!.textContent).toContain(
      'The office has to close after it opens, on the same day.',
    );

    await type('#closes', '17:00');
    for (const day of days().filter((d) => d.checked)) {
      day.click();
    }
    await fixture.whenStable();
    await submit(officeForm());
    expect(element.querySelector('.workdays .field-error')!.textContent).toContain(
      'Pick at least one workday.',
    );

    expect(setOfficeHours).not.toHaveBeenCalled();
  });

  it("puts the API's word on the field it's about", async () => {
    setOfficeHours.mockReturnValue(
      throwError(
        () => new ApiError(400, 'Bad request', null, { TimeZone: ['Unknown time zone.'] }),
      ),
    );
    await render();

    await submit(officeForm());

    expect(element.querySelector('#zone-error')!.textContent).toContain('Unknown time zone.');
  });

  it("saves a kind's rules, the longest booking in hours sent as minutes", async () => {
    await render();

    await type('#hours-type-desk', '1.5');
    await submit(card('Desk').querySelector('form')!);

    expect(setTypeRules).toHaveBeenCalledWith('type-desk', {
      maxDurationMinutes: 90,
      maxDaysAhead: 7,
      maxActivePerUser: 2,
      officeHoursOnly: true,
    });
    expect(success).toHaveBeenCalledWith('Rules for Desk saved.');
  });

  it('sends an empty box as no rule of its own', async () => {
    await render();

    await submit(card('Company Car').querySelector('form')!);

    expect(setTypeRules).toHaveBeenCalledWith('type-car', {
      maxDurationMinutes: null,
      maxDaysAhead: null,
      maxActivePerUser: null,
      officeHoursOnly: false,
    });
  });

  it('keeps the rules within those for every booking before asking', async () => {
    await render();

    await type('#hours-type-desk', '1.1');
    await type('#ahead-type-desk', '181');
    await type('#per-person-type-desk', '0');
    await submit(card('Desk').querySelector('form')!);

    const errors = Array.from(card('Desk').querySelectorAll('.field-error')).map((e) =>
      e.textContent!.trim(),
    );
    expect(errors).toEqual([
      'From 0.25 to 744 hours, in quarter hours.',
      'A whole number from 0 (today only) to 180.',
      'A whole number from 1 to 100.',
    ]);
    expect(setTypeRules).not.toHaveBeenCalled();
  });

  it("shows the API's reasons on the kind's card", async () => {
    setTypeRules.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'Bad request', null, {
            MaxDaysAhead: ['Days ahead has to be from 0 (today only) to 180.'],
          }),
      ),
    );
    await render();

    await submit(card('Desk').querySelector('form')!);

    expect(card('Desk').querySelector('[role=alert]')!.textContent).toContain(
      'Days ahead has to be from 0 (today only) to 180.',
    );
  });

  it('says when the rules could not be loaded, and tries again', async () => {
    get.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.querySelector('[role=alert]')!.textContent).toContain(
      'The rules could not be loaded.',
    );

    element.querySelector<HTMLButtonElement>('[role=alert] button')!.click();
    await fixture.whenStable();

    expect(get).toHaveBeenCalledTimes(2);
    expect(element.querySelectorAll('.kind')).toHaveLength(3);
  });
});
