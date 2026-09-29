import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { AccountService } from '../../../core/services/account.service';
import { AuthService } from '../../../core/services/auth.service';
import { ResourceService } from '../../../core/services/resource.service';
import { account } from '../../../../testing/account-fixtures';
import { MEETING_ROOM_TYPE, fakeResource } from '../../../../testing/resource-fixtures';
import { simulateTurkishBrowser } from '../../../../testing/turkish-locale';
import { ResourceForm } from './resource-form';

describe('ResourceForm', () => {
  const isAdmin = signal(true);
  const isManager = signal(false);
  const currentUser = signal<{ id: string } | null>({ id: 'u-admin' });
  const zeynep = account({
    id: 'u-zeynep',
    firstName: 'Zeynep',
    lastName: 'Kaya',
    role: 'Manager',
  });
  const okan = account({ id: 'u-okan', firstName: 'Okan', lastName: 'Yıldız', role: 'Manager' });

  let create: ReturnType<typeof vi.fn>;
  let update: ReturnType<typeof vi.fn>;
  let setTeams: ReturnType<typeof vi.fn>;
  let getResource: ReturnType<typeof vi.fn>;
  let getAccounts: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ResourceForm>;
  let element: HTMLElement;

  beforeEach(async () => {
    isAdmin.set(true);
    isManager.set(false);
    currentUser.set({ id: 'u-admin' });
    create = vi.fn(() => of(fakeResource()));
    update = vi.fn(() => of(fakeResource()));
    setTeams = vi.fn(() => of(fakeResource()));
    getResource = vi.fn(() =>
      of(fakeResource({ description: 'Big screen', status: 'Maintenance' })),
    );
    getAccounts = vi.fn(() => of([zeynep, okan]));

    await TestBed.configureTestingModule({
      imports: [ResourceForm],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAdmin, isManager, currentUser } },
        { provide: AccountService, useValue: { getAccounts } },
        {
          provide: ResourceService,
          useValue: {
            getResourceTypes: () => of([MEETING_ROOM_TYPE]),
            getResource,
            create,
            update,
            setTeams,
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
      teamIds: [],
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

  it('explains a refused switch-off instead of a generic failure', async () => {
    const message =
      '1 confirmed reservation(s) on Meeting Room A from now on. Cancel or move them first.';
    update.mockReturnValue(throwError(() => new ApiError(409, 'Conflict', message)));
    await render('res-1');
    await type('#status', 'Inactive');
    await submit();

    expect(element.querySelector('[role="alert"]')!.textContent!.trim()).toBe(message);
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('shows an error when the resource to edit cannot be loaded', async () => {
    getResource.mockReturnValue(throwError(() => new ApiError(404, 'Not found')));
    await render('missing');

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    expect(element.querySelector('form')).toBeNull();
  });

  describe('teams', () => {
    function teamBoxes(): HTMLInputElement[] {
      return Array.from(element.querySelectorAll<HTMLInputElement>('.team-option input'));
    }

    function teamNames(): string[] {
      return Array.from(element.querySelectorAll('.team-option')).map((o) =>
        o.textContent!.replace(/\s+/g, ' ').trim(),
      );
    }

    async function tick(name: string): Promise<void> {
      const option = Array.from(element.querySelectorAll('.team-option')).find((o) =>
        o.textContent!.includes(name),
      )!;
      option.querySelector('input')!.click();
      await fixture.whenStable();
    }

    it("offers an admin the active managers' teams and creates the resource for the ticked ones", async () => {
      await render();

      expect(getAccounts).toHaveBeenCalledWith({ role: 'Manager', status: 'Active' });
      expect(teamNames()).toEqual(["Okan Yıldız's team", "Zeynep Kaya's team"]);
      expect(teamBoxes().some((box) => box.checked)).toBe(false);

      await fillValidForm();
      await tick('Zeynep');
      await submit();

      expect(create).toHaveBeenCalledWith(expect.objectContaining({ teamIds: [zeynep.id] }));
    });

    it('ticks the teams a resource already has and leaves them alone when unchanged', async () => {
      getResource.mockReturnValue(
        of(fakeResource({ teams: [{ id: zeynep.id, managerName: 'Zeynep Kaya' }] })),
      );
      await render('res-1');

      expect(teamBoxes().map((box) => box.checked)).toEqual([false, true]);

      await submit();

      expect(update).toHaveBeenCalled();
      expect(setTeams).not.toHaveBeenCalled();
      expect(navigateByUrl).toHaveBeenCalledWith('/resources');
    });

    it('saves the details, then the new teams', async () => {
      getResource.mockReturnValue(
        of(fakeResource({ teams: [{ id: zeynep.id, managerName: 'Zeynep Kaya' }] })),
      );
      await render('res-1');

      await tick('Okan');
      await tick('Zeynep');
      await submit();

      expect(update).toHaveBeenCalledBefore(setTeams);
      expect(setTeams).toHaveBeenCalledWith('res-1', [okan.id]);
      expect(navigateByUrl).toHaveBeenCalledWith('/resources');
    });

    it('shares a resource with everyone when every team is unticked', async () => {
      getResource.mockReturnValue(
        of(fakeResource({ teams: [{ id: zeynep.id, managerName: 'Zeynep Kaya' }] })),
      );
      await render('res-1');

      await tick('Zeynep');
      await submit();

      expect(setTeams).toHaveBeenCalledWith('res-1', []);
    });

    it("keeps a team whose manager isn't active any more visible, so it can be unticked", async () => {
      getResource.mockReturnValue(
        of(fakeResource({ teams: [{ id: 'u-left', managerName: 'Deniz Arslan' }] })),
      );
      await render('res-1');

      expect(teamNames()).toContain("Deniz Arslan's team (no longer an active manager)");
      expect(teamBoxes().filter((box) => box.checked).length).toBe(1);
    });

    it('says the details were saved when only the teams were refused', async () => {
      getResource.mockReturnValue(of(fakeResource()));
      setTeams.mockReturnValue(
        throwError(
          () =>
            new ApiError(400, 'Invalid', null, {
              TeamIds: ["Every team must be an active manager's."],
            }),
        ),
      );
      await render('res-1');

      await tick('Okan');
      await submit();

      expect(update).toHaveBeenCalled();
      expect(element.querySelector('[role="alert"]')!.textContent!.trim()).toBe(
        'Your other changes were saved, but the teams were not.',
      );
      expect(element.querySelector('.teams .field-error')!.textContent).toContain(
        "Every team must be an active manager's.",
      );
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it("keeps to the API's 20 teams per resource", async () => {
      getAccounts.mockReturnValue(
        of(
          Array.from({ length: 21 }, (_, i) =>
            account({ id: `u-m${i}`, firstName: 'Manager', lastName: `${i + 10}` }),
          ),
        ),
      );
      await render();
      await fillValidForm();
      const [extra, ...twenty] = teamBoxes();
      for (const box of twenty) {
        box.click();
      }
      await fixture.whenStable();
      expect(element.textContent).not.toContain('at most 20 teams');

      extra.click();
      await fixture.whenStable();
      await submit();

      expect(element.textContent).toContain('A resource can belong to at most 20 teams.');
      expect(create).not.toHaveBeenCalled();
    });

    it('says so when the teams cannot be loaded', async () => {
      getAccounts.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render();

      expect(element.textContent).toContain('The teams could not be loaded.');
    });

    it("still shows the resource's own teams when the managers cannot be loaded", async () => {
      getAccounts.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      getResource.mockReturnValue(
        of(fakeResource({ teams: [{ id: zeynep.id, managerName: 'Zeynep Kaya' }] })),
      );
      await render('res-1');

      expect(teamNames()).toEqual(["Zeynep Kaya's team"]);
      expect(teamBoxes()[0].checked).toBe(true);
    });

    it('says the details were saved when the teams then fail for another reason', async () => {
      getResource.mockReturnValue(of(fakeResource()));
      setTeams.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
      await render('res-1');

      await tick('Okan');
      await submit();

      expect(element.querySelector('[role="alert"]')!.textContent!.trim()).toBe(
        'Your other changes were saved, but the teams were not.',
      );
    });

    describe('for a manager', () => {
      beforeEach(() => {
        isAdmin.set(false);
        isManager.set(true);
        currentUser.set({ id: zeynep.id });
      });

      it('adds a resource to their own team without asking for teams', async () => {
        await render();

        expect(getAccounts).not.toHaveBeenCalled();
        expect(element.querySelector('.teams')).toBeNull();
        expect(element.querySelector('.team-note')!.textContent).toContain(
          'It will belong to your team',
        );

        await fillValidForm();
        await submit();

        expect(create).toHaveBeenCalledWith(
          expect.not.objectContaining({ teamIds: expect.anything() }),
        );
      });

      it("edits their own team's resource, never its teams", async () => {
        getResource.mockReturnValue(
          of(fakeResource({ teams: [{ id: zeynep.id, managerName: 'Zeynep Kaya' }] })),
        );
        await render('res-1');

        expect(element.querySelector('.team-note')!.textContent).toContain(
          'It belongs to your team',
        );
        await submit();

        expect(update).toHaveBeenCalled();
        expect(setTeams).not.toHaveBeenCalled();
      });

      it('explains instead of offering a form for a resource that is shared or used by other teams', async () => {
        for (const teams of [
          [],
          [
            { id: zeynep.id, managerName: 'Zeynep Kaya' },
            { id: okan.id, managerName: 'Okan Yıldız' },
          ],
        ]) {
          getResource.mockReturnValue(of(fakeResource({ teams })));
          await render('res-1');

          expect(element.querySelector('form')).toBeNull();
          expect(element.querySelector('.read-only')!.textContent).toContain(
            'Only an administrator can change this resource',
          );
          fixture.destroy();
        }
      });
    });
  });
});
