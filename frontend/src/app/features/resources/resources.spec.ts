import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { Resources } from './resources';

describe('Resources', () => {
  const isAdmin = signal(false);
  const room = fakeResource();
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    capacity: 1,
    location: '3rd Floor',
    features: [],
    status: 'Maintenance',
  });

  let getResources: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Resources>;
  let element: HTMLElement;

  beforeEach(async () => {
    isAdmin.set(false);
    getResources = vi.fn(() => of([room, desk]));
    update = vi.fn();

    await TestBed.configureTestingModule({
      imports: [Resources],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAdmin } },
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([DESK_TYPE, MEETING_ROOM_TYPE]),
            getResources,
            update,
          },
        },
      ],
    }).compileComponents();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Resources);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function rowNames(): string[] {
    return Array.from(element.querySelectorAll('tbody .name')).map((n) => n.textContent!.trim());
  }

  async function select(id: string, value: string): Promise<void> {
    const el = element.querySelector<HTMLSelectElement>(id)!;
    el.value = value;
    el.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('lists every resource with its status', async () => {
    await render();

    expect(rowNames()).toEqual(['Meeting Room A', 'Desk 12']);
    expect(element.textContent).toContain('Maintenance');
    expect(getResources).toHaveBeenCalledWith({ resourceTypeId: '', status: null });
  });

  it('reloads from the API when the type or status filter changes', async () => {
    await render();

    await select('#type', DESK_TYPE.id);
    expect(getResources).toHaveBeenLastCalledWith({ resourceTypeId: DESK_TYPE.id, status: null });

    await select('#status', 'Active');
    expect(getResources).toHaveBeenLastCalledWith({
      resourceTypeId: DESK_TYPE.id,
      status: 'Active',
    });
  });

  it('searches name, location and features without calling the API', async () => {
    await render();
    const calls = getResources.mock.calls.length;

    const search = element.querySelector<HTMLInputElement>('#search')!;
    search.value = 'video';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(rowNames()).toEqual(['Meeting Room A']);
    expect(getResources.mock.calls.length).toBe(calls);
  });

  it('shows an error when loading fails', async () => {
    getResources.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });

  it('hides admin actions from employees', async () => {
    await render();

    expect(element.textContent).not.toContain('New resource');
    expect(element.querySelector('.actions')).toBeNull();
  });

  it('lets an admin deactivate a resource, sending the full resource', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    update.mockReturnValue(of({ ...room, status: 'Inactive' }));
    await render();

    const button = Array.from(element.querySelectorAll<HTMLButtonElement>('.actions button')).find(
      (b) => b.textContent!.includes('Deactivate'),
    )!;
    button.click();
    await fixture.whenStable();

    const { id, resourceTypeName: _, ...fields } = room;
    expect(update).toHaveBeenCalledWith(id, { ...fields, status: 'Inactive' });
    expect(element.querySelector('tbody tr')?.textContent).toContain('Inactive');
  });

  it('does nothing when the admin cancels the confirmation', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    await render();

    element.querySelector<HTMLButtonElement>('.actions button')!.click();

    expect(update).not.toHaveBeenCalled();
  });
});
