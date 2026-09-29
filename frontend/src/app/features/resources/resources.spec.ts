import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { ResourceService } from '../../core/services/resource.service';
import { DESK_TYPE, MEETING_ROOM_TYPE, fakeResource } from '../../../testing/resource-fixtures';
import { simulateTurkishBrowser } from '../../../testing/turkish-locale';
import { Resources } from './resources';

describe('Resources', () => {
  const isAdmin = signal(false);
  const isManager = signal(false);
  const currentUser = signal<{ id: string } | null>({ id: 'u-ayse' });
  const zeynep = { id: 'u-zeynep', managerName: 'Zeynep Kaya' };
  const okan = { id: 'u-okan', managerName: 'Okan Yıldız' };
  const room = fakeResource();
  const desk = fakeResource({
    id: 'res-2',
    name: 'Desk 12',
    resourceTypeId: DESK_TYPE.id,
    resourceTypeName: DESK_TYPE.name,
    capacity: 1,
    location: '3rd Floor',
    features: ['HDMI'],
    status: 'Maintenance',
  });

  let getResources: ReturnType<typeof vi.fn>;
  let changeStatus: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Resources>;
  let element: HTMLElement;

  beforeEach(async () => {
    isAdmin.set(false);
    isManager.set(false);
    currentUser.set({ id: 'u-ayse' });
    getResources = vi.fn(() => of([room, desk]));
    changeStatus = vi.fn((id: string, status: string) =>
      of({ ...[room, desk].find((r) => r.id === id)!, status }),
    );

    await TestBed.configureTestingModule({
      imports: [Resources],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAdmin, isManager, currentUser } },
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([DESK_TYPE, MEETING_ROOM_TYPE]),
            getResources,
            changeStatus,
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

  it('matches a capital I in a Turkish browser ("hdmi" finds "HDMI")', async () => {
    const restore = simulateTurkishBrowser();
    try {
      await render();
      const search = element.querySelector<HTMLInputElement>('#search')!;
      search.value = 'hdmi';
      search.dispatchEvent(new Event('input'));
      await fixture.whenStable();

      expect(rowNames()).toEqual(['Desk 12']);
    } finally {
      restore();
    }
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

  function rowFor(name: string): HTMLTableRowElement {
    return Array.from(element.querySelectorAll<HTMLTableRowElement>('tbody tr')).find(
      (row) => row.querySelector('.name')!.textContent!.trim() === name,
    )!;
  }

  it("shows whose team each resource belongs to, or that it's shared", async () => {
    getResources.mockReturnValue(
      of([
        fakeResource({ teams: [zeynep] }),
        fakeResource({ id: 'res-2', name: 'Projector P-1', teams: [zeynep, okan] }),
        desk,
      ]),
    );
    await render();

    expect(rowFor('Meeting Room A').querySelector('.team')!.textContent!.trim()).toBe(
      "Zeynep Kaya's team",
    );
    expect(rowFor('Projector P-1').querySelector('.team')!.textContent!.trim()).toBe(
      'Zeynep Kaya, Okan Yıldız',
    );
    expect(rowFor('Desk 12').querySelector('.team')!.textContent!.trim()).toBe('Shared');
  });

  it("lets a manager add resources and change only their own team's", async () => {
    isManager.set(true);
    currentUser.set({ id: zeynep.id });
    getResources.mockReturnValue(
      of([
        fakeResource({ teams: [zeynep] }),
        fakeResource({ id: 'res-2', name: 'Projector P-1', teams: [zeynep, okan] }),
        fakeResource({ id: 'res-3', name: 'Van', teams: [okan] }),
        desk,
      ]),
    );
    await render();

    expect(element.querySelector('a[href="/resources/new"]')).not.toBeNull();
    expect(
      rowFor('Meeting Room A').querySelector('a[href="/resources/res-1/edit"]'),
    ).not.toBeNull();
    expect(rowFor('Meeting Room A').querySelector('.actions button')).not.toBeNull();
    for (const name of ['Projector P-1', 'Van', 'Desk 12']) {
      expect(rowFor(name).querySelector('.actions')!.children.length).toBe(0);
    }
  });

  it('lets an admin change every resource, whoever it belongs to', async () => {
    isAdmin.set(true);
    currentUser.set({ id: 'u-admin' });
    getResources.mockReturnValue(of([fakeResource({ teams: [zeynep, okan] }), desk]));
    await render();

    expect(element.querySelectorAll('.actions a').length).toBe(2);
    expect(element.querySelectorAll('.actions button').length).toBe(2);
  });

  function actionButton(label: string): HTMLButtonElement {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('.actions button')).find((b) =>
      b.textContent!.includes(label),
    )!;
  }

  it('deactivates with a status-only change, so edits made since the list loaded are kept', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();

    actionButton('Deactivate').click();
    await fixture.whenStable();

    expect(changeStatus).toHaveBeenCalledWith(room.id, 'Inactive');
    expect(element.querySelector('tbody tr')?.textContent).toContain('Inactive');
  });

  it('activates a resource that is not Active', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();

    actionButton('Activate').click();
    await fixture.whenStable();

    expect(changeStatus).toHaveBeenCalledWith(desk.id, 'Active');
    expect(element.querySelectorAll('tbody tr')[1].textContent).toContain('Active');
  });

  it('drops a resource that no longer matches the status filter', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    getResources.mockReturnValue(of([room]));
    await render();
    await select('#status', 'Active');

    actionButton('Deactivate').click();
    await fixture.whenStable();

    expect(rowNames()).toEqual([]);
  });

  it('does nothing when the admin cancels the confirmation', async () => {
    isAdmin.set(true);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    await render();

    element.querySelector<HTMLButtonElement>('.actions button')!.click();

    expect(changeStatus).not.toHaveBeenCalled();
  });
});
