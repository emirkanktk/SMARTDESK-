import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { qrModules } from '../../shared/utils/qr';
import { QrLabels } from './qr-labels';

describe('QrLabels', () => {
  const board = fakeResource({ id: 'board', name: 'Board Room', checkInRequired: true });
  const desk = fakeResource({
    id: 'desk',
    name: 'Desk 3-01',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    location: '3rd Floor',
  });
  const studio = fakeResource({
    id: 'studio',
    name: 'Studio',
    teams: [{ id: 'u-selin', managerName: 'Selin Kaya' }],
  });
  const off = fakeResource({ id: 'off', name: 'Old Room', status: 'Inactive' });
  const url = (id: string) => `http://localhost:4200/check-in/${id}?code=c0de`;

  const isAdmin = signal(true);
  const isManager = signal(false);
  let getResources: ReturnType<typeof vi.fn>;
  let getCheckInCode: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    isAdmin.set(true);
    isManager.set(false);
    getResources = vi.fn(() => of([studio, off, desk, board]));
    getCheckInCode = vi.fn((id: string) =>
      of({ code: 'c0de', url: url(id), doorScreenSince: null }),
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { currentUser: signal({ id: 'u-selin' }), isAdmin, isManager },
        },
        {
          provide: ResourceService,
          useValue: {
            getResources,
            getResourceTypes: () => of([MEETING_ROOM_TYPE, DESK_TYPE]),
            getCheckInCode,
          },
        },
      ],
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(QrLabels);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();
  const names = (element: HTMLElement) =>
    [...element.querySelectorAll('.label .resource-name')].map((n) => text(n));

  it("puts every resource still in use on a label, by name, with its QR code and what it's for", async () => {
    const element = await render();

    expect(names(element)).toEqual(['Board Room', 'Desk 3-01', 'Studio']);
    expect(getCheckInCode).not.toHaveBeenCalledWith('off');
    const [first, second] = [...element.querySelectorAll('.label')];
    expect(first.querySelector('app-qr-code path')!.getAttribute('d')).toBe(
      qrModules(url('board')).path,
    );
    expect(first.querySelector('app-qr-code svg')!.getAttribute('aria-label')).toBe(
      'QR code for Board Room',
    );
    expect(text(first.querySelector('.how')!)).toBe('Scan to check in, or to report a problem.');
    expect(text(second.querySelector('.resource-location')!)).toBe('3rd Floor');
    expect(text(second.querySelector('.how')!)).toBe(
      'Scan to see its day, book it, or report a problem.',
    );
    expect(text(element.querySelector('.summary')!)).toBe(
      '3 labels, on 1 A4 sheet (8 to a sheet).',
    );
  });

  it("gives a manager their own team's resources only", async () => {
    isAdmin.set(false);
    isManager.set(true);
    const element = await render();

    expect(names(element)).toEqual(['Studio']);
    expect(getCheckInCode).toHaveBeenCalledTimes(1);
  });

  it('prints the ones picked: one by one, a type, all or none', async () => {
    const element = await render();
    const box = element.querySelector<HTMLSelectElement>('.pick select')!;
    const pick = async (value: string) => {
      box.value = value;
      box.dispatchEvent(new Event('change'));
      await new Promise((resolve) => setTimeout(resolve));
    };

    await pick(DESK_TYPE.id);
    expect(names(element)).toEqual(['Desk 3-01']);
    expect(box.value).toBe('');

    await pick('none');
    expect(names(element)).toEqual([]);
    expect(element.querySelector<HTMLButtonElement>('.toolbar button')!.disabled).toBe(true);

    await pick('all');
    const studioBox = [...element.querySelectorAll<HTMLLabelElement>('.choice')]
      .find((label) => text(label) === 'Studio')!
      .querySelector('input')!;
    studioBox.click();
    await new Promise((resolve) => setTimeout(resolve));
    expect(names(element)).toEqual(['Board Room', 'Desk 3-01']);
  });

  it('says how many sheets, and which codes could not be loaded', async () => {
    const many = Array.from({ length: 9 }, (_, i) =>
      fakeResource({ id: `r${i}`, name: `Room ${i + 1}` }),
    );
    getResources.mockReturnValue(of(many));
    getCheckInCode.mockImplementation((id: string) =>
      id === 'r0'
        ? throwError(() => new ApiError(500, 'Server error'))
        : of({ code: 'c0de', url: url(id), doorScreenSince: null }),
    );
    const element = await render();

    expect([...element.querySelector('.summary')!.children].map((part) => text(part))).toEqual([
      '9 labels, on 2 A4 sheets (8 to a sheet).',
      'The code of 1 could not be loaded.',
    ]);
    expect(element.querySelectorAll('.label app-qr-code')).toHaveLength(8);
  });

  it('prints from its button, and says so when the resources could not be loaded', async () => {
    const print = vi.fn();
    vi.stubGlobal('print', print);
    const element = await render();
    element.querySelector<HTMLButtonElement>('.toolbar button')!.click();
    expect(print).toHaveBeenCalled();

    getResources.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    const failed = await render();
    expect(failed.textContent).toContain('The resources could not be loaded.');
    expect(failed.querySelector<HTMLButtonElement>('.toolbar button')!.disabled).toBe(true);
  });
});
