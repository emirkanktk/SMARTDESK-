import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { ResourceService } from '../../../core/services/resource.service';
import { MEETING_ROOM_TYPE, fakeResource } from '../../../../testing/resource-fixtures';
import { simulateTurkishBrowser } from '../../../../testing/turkish-locale';
import { ResourceForm } from './resource-form';

describe('ResourceForm', () => {
  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let getResource: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ResourceForm>;
  let element: HTMLElement;

  beforeEach(async () => {
    create = vi.fn(() => of(fakeResource()));
    update = vi.fn(() => of(fakeResource()));
    getResource = vi.fn(() =>
      of(fakeResource({ description: 'Big screen', status: 'Maintenance' })),
    );

    await TestBed.configureTestingModule({
      imports: [ResourceForm],
      providers: [
        provideRouter([]),
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([MEETING_ROOM_TYPE]),
            getResource,
            create,
            update,
          },
        },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  async function render(id?: string): Promise<void> {
    fixture = TestBed.createComponent(ResourceForm);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  async function addFeature(value: string): Promise<void> {
    await type('#feature', value);
    element
      .querySelector('#feature')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();
  }

  async function fillValidForm(): Promise<void> {
    await type('#name', '  Meeting Room B ');
    await type('#type', MEETING_ROOM_TYPE.id);
    await type('#capacity', '6');
    await type('#location', '1st Floor');
  }

  it('shows required-field errors and does not call the API', async () => {
    await render();
    await type('#capacity', '');
    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(element.querySelectorAll('.field-error').length).toBeGreaterThanOrEqual(4);
  });

  it('rejects a capacity outside 1-1000', async () => {
    await render();
    await fillValidForm();
    await type('#capacity', '1001');
    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(element.textContent).toContain('between 1 and 1000');
  });

  it('creates a resource with trimmed values and features, then goes back to the list', async () => {
    await render();
    await fillValidForm();
    await addFeature('TV');
    await addFeature('Whiteboard');
    await submit();

    expect(create).toHaveBeenCalledWith({
      name: 'Meeting Room B',
      resourceTypeId: MEETING_ROOM_TYPE.id,
      capacity: 6,
      location: '1st Floor',
      description: null,
      features: ['TV', 'Whiteboard'],
    });
    expect(navigateByUrl).toHaveBeenCalledWith('/resources');
  });

  it('refuses a duplicate feature regardless of case', async () => {
    await render();
    await addFeature('TV');
    await addFeature('tv');

    expect(element.querySelectorAll('.chip').length).toBe(1);
    expect(element.textContent).toContain('already in the list');
  });

  it('refuses "wifi" after "WIFI" in a Turkish browser, like the API does', async () => {
    const restore = simulateTurkishBrowser();
    try {
      await render();
      await addFeature('WIFI');
      await addFeature('wifi');

      expect(element.querySelectorAll('.chip').length).toBe(1);
      expect(element.textContent).toContain('already in the list');
    } finally {
      restore();
    }
  });

  it('removes a feature', async () => {
    await render();
    await addFeature('TV');
    element.querySelector<HTMLButtonElement>('.chip-remove')!.click();
    await fixture.whenStable();

    expect(element.querySelectorAll('.chip').length).toBe(0);
  });

  it('shows server validation messages next to the field', async () => {
    create.mockReturnValue(
      throwError(() => new ApiError(400, 'Invalid', null, { Name: ['Name is too long.'] })),
    );
    await render();
    await fillValidForm();
    await submit();

    expect(element.textContent).toContain('Name is too long.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('asks for a whole number of people', async () => {
    await render();
    await fillValidForm();
    await type('#capacity', '2.5');
    await submit();

    expect(element.textContent).toContain('Enter a whole number.');
    expect(create).not.toHaveBeenCalled();
  });

  it('stops at 20 features, like the API', async () => {
    await render();
    for (let i = 1; i <= 20; i++) {
      await addFeature(`Feature ${i}`);
    }
    expect(element.textContent).not.toContain('at most 20 features');

    await addFeature('Feature 21');

    expect(element.textContent).toContain('A resource can have at most 20 features.');
    expect(element.textContent).not.toContain('Feature 21');
  });

  it('keeps the API messages no field can take above the form', async () => {
    create.mockReturnValue(
      throwError(() => new ApiError(400, 'Invalid', null, { Other: ['Something else is wrong.'] })),
    );
    await render();
    await fillValidForm();
    await submit();

    expect(element.querySelector('[role="alert"]')!.textContent!.trim()).toBe(
      'Something else is wrong.',
    );
  });

  it('keeps a note on the form when saving fails otherwise', async () => {
    create.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
    await render();
    await fillValidForm();
    await submit();

    expect(element.textContent).toContain('The resource could not be saved.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('says so when the resource types cannot be loaded', async () => {
    TestBed.inject(ResourceService).getResourceTypes = () =>
      throwError(() => new ApiError(500, 'Server error'));
    await render();

    expect(element.textContent).toContain('Resource types could not be loaded.');
  });

  it('loads an existing resource and saves it with its status', async () => {
    await render('res-1');

    expect(getResource).toHaveBeenCalledWith('res-1');
    expect(element.querySelector<HTMLInputElement>('#name')!.value).toBe('Meeting Room A');
    expect(element.querySelector<HTMLSelectElement>('#status')!.value).toBe('Maintenance');

    await type('#status', 'Active');
    await submit();

    expect(update).toHaveBeenCalledWith('res-1', {
      name: 'Meeting Room A',
      resourceTypeId: MEETING_ROOM_TYPE.id,
      capacity: 8,
      location: '2nd Floor',
      description: 'Big screen',
      features: ['TV', 'Video Conference'],
      status: 'Active',
    });
  });

  it('shows an error when the resource to edit cannot be loaded', async () => {
    getResource.mockReturnValue(throwError(() => new ApiError(404, 'Not found')));
    await render('missing');

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    expect(element.querySelector('form')).toBeNull();
  });
});
