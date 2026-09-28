import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { account } from '../../../testing/account-fixtures';
import { UserAccount } from '../../core/models/account.model';
import { ApiError } from '../../core/models/api-error.model';
import { AccountService } from '../../core/services/account.service';
import { NotificationService } from '../../core/services/notification.service';
import { Team } from './team';

describe('Team', () => {
  const mehmet = account({ managerId: 'u-zeynep', managerName: 'Zeynep Kaya' });
  const ayse = account({
    id: 'u-ayse',
    firstName: 'Ayşe',
    lastName: 'Yılmaz',
    email: 'ayse@company.example',
    managerId: 'u-zeynep',
  });
  const off = account({
    id: 'u-selin',
    firstName: 'Selin',
    lastName: 'Ak',
    email: 'selin@company.example',
    status: 'Disabled',
    managerId: 'u-zeynep',
  });
  const request = account({
    id: 'u-deniz',
    firstName: 'Deniz',
    lastName: 'Arslan',
    email: 'deniz@company.example',
    status: 'PendingApproval',
    managerId: 'u-zeynep',
  });

  let accounts: Record<string, ReturnType<typeof vi.fn>>;
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Team>;
  let element: HTMLElement;

  beforeEach(async () => {
    accounts = {
      getTeam: vi.fn(() => of([mehmet, off, request, ayse])),
      addTeamMember: vi.fn(() =>
        of(
          account({
            id: 'u-elif',
            firstName: 'Elif',
            lastName: 'Şahin',
            email: 'elif@company.example',
            mustChangePassword: true,
          }),
        ),
      ),
      removeTeamMember: vi.fn(() => of(undefined)),
      setTeamMemberEnabled: vi.fn((id: string, enabled: boolean) =>
        of({ ...mehmet, id, status: enabled ? 'Active' : 'Disabled' }),
      ),
      setTeamMemberTemporaryPassword: vi.fn((id: string) =>
        of({ ...ayse, id, mustChangePassword: true }),
      ),
      approveRequest: vi.fn((id: string) => of({ ...request, id, status: 'Active' })),
      rejectRequest: vi.fn(() => of(undefined)),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await TestBed.configureTestingModule({
      imports: [Team],
      providers: [
        { provide: AccountService, useValue: accounts },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Team);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => vi.restoreAllMocks());

  const rows = () => Array.from(element.querySelectorAll<HTMLTableRowElement>('tbody tr'));
  const rowOf = (who: UserAccount) => rows().find((r) => r.textContent!.includes(who.email));
  const requestItems = () => Array.from(element.querySelectorAll<HTMLElement>('.request-list li'));
  const button = (scope: HTMLElement | undefined, label: string) =>
    Array.from(scope!.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === label,
    );

  async function click(target: HTMLElement | undefined): Promise<void> {
    target!.click();
    await fixture.whenStable();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submitAddForm(): Promise<void> {
    element.querySelector('.add-form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('lists requests to join apart from the members, each by name', () => {
    expect(requestItems().map((li) => li.querySelector('.name')!.textContent!.trim())).toEqual([
      'Deniz Arslan',
    ]);
    expect(rows().map((r) => r.querySelector('.name')!.textContent!.trim())).toEqual([
      'Ayşe Yılmaz',
      'Mehmet Demir',
      'Selin Ak',
    ]);
    expect(element.querySelector('.count')!.textContent).toContain('3 members');
  });

  it('shows no requests section when there are none', async () => {
    accounts['getTeam'].mockReturnValue(of([mehmet]));
    fixture = TestBed.createComponent(Team);
    element = fixture.nativeElement;
    await fixture.whenStable();

    expect(element.querySelector('.requests')).toBeNull();
  });

  it('approves a request, which then shows as a member', async () => {
    await click(button(requestItems()[0], 'Approve'));

    expect(accounts['approveRequest']).toHaveBeenCalledWith('u-deniz');
    expect(element.querySelector('.requests')).toBeNull();
    expect(rowOf(request)).toBeDefined();
  });

  it('rejects a request only after confirming', async () => {
    vi.mocked(window.confirm).mockReturnValueOnce(false);
    await click(button(requestItems()[0], 'Reject'));
    expect(accounts['rejectRequest']).not.toHaveBeenCalled();

    await click(button(requestItems()[0], 'Reject'));
    expect(accounts['rejectRequest']).toHaveBeenCalledWith('u-deniz');
    expect(element.querySelector('.requests')).toBeNull();
  });

  it('says so when nobody is in the team yet', async () => {
    accounts['getTeam'].mockReturnValue(of([]));
    fixture = TestBed.createComponent(Team);
    element = fixture.nativeElement;
    await fixture.whenStable();

    expect(element.textContent).toContain('Nobody is in your team yet.');
  });

  it('adds a member with trimmed details and their temporary password', async () => {
    await type('#member-first-name', ' Elif ');
    await type('#member-last-name', 'Şahin');
    await type('#member-email', ' elif@company.example ');
    await type('#member-password', 'Temp-Pass-2026');
    await submitAddForm();

    expect(accounts['addTeamMember']).toHaveBeenCalledWith({
      firstName: 'Elif',
      lastName: 'Şahin',
      email: 'elif@company.example',
      password: 'Temp-Pass-2026',
    });
    expect(rowOf(account({ email: 'elif@company.example' }))!.textContent).toContain(
      'Temporary password',
    );
    expect(notifications.success).toHaveBeenCalledWith(
      expect.stringContaining('Give them the temporary password'),
    );
    expect(element.querySelector<HTMLInputElement>('#member-first-name')!.value).toBe('');
  });

  it('checks the form, the password rules included, before asking the API', async () => {
    await submitAddForm();
    expect(accounts['addTeamMember']).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Type or generate a password.');

    await type('#member-password', 'short');
    expect(element.querySelector('#member-password-error')!.textContent).toContain('Add ');
  });

  it('generates a temporary password that meets the rules', async () => {
    await click(button(element.querySelector<HTMLElement>('.add-form')!, 'Generate'));

    expect(element.querySelector<HTMLInputElement>('#member-password')!.value).toMatch(
      /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{12}$/,
    );
  });

  it('shows a taken email in the API’s words', async () => {
    accounts['addTeamMember'].mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', 'A user with this email already exists.')),
    );
    await type('#member-first-name', 'Selin');
    await type('#member-last-name', 'Again');
    await type('#member-email', 'selin@company.example');
    await type('#member-password', 'Temp-Pass-2026');
    await submitAddForm();

    expect(element.querySelector('.add-form [role="alert"]')!.textContent).toContain(
      'A user with this email already exists.',
    );
  });

  it('sets a member’s temporary password', async () => {
    await click(button(rowOf(ayse), 'Set password'));
    const input = element.querySelector<HTMLInputElement>('app-temporary-password-form input')!;
    input.value = 'Temp-Pass-2026';
    input.dispatchEvent(new Event('input'));
    element.querySelector('app-temporary-password-form form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(accounts['setTeamMemberTemporaryPassword']).toHaveBeenCalledWith(
      'u-ayse',
      'Temp-Pass-2026',
    );
    expect(element.querySelector('app-temporary-password-form')).toBeNull();
    expect(rowOf(ayse)!.textContent).toContain('Temporary password');
  });

  it('closes the password form on cancel without asking the API', async () => {
    await click(button(rowOf(ayse), 'Set password'));
    await click(
      button(element.querySelector<HTMLElement>('app-temporary-password-form')!, 'Cancel'),
    );

    expect(element.querySelector('app-temporary-password-form')).toBeNull();
    expect(accounts['setTeamMemberTemporaryPassword']).not.toHaveBeenCalled();
  });

  it('disables after confirming, and enables a disabled member', async () => {
    vi.mocked(window.confirm).mockReturnValueOnce(false);
    await click(button(rowOf(mehmet), 'Disable'));
    expect(accounts['setTeamMemberEnabled']).not.toHaveBeenCalled();

    await click(button(rowOf(mehmet), 'Disable'));
    expect(accounts['setTeamMemberEnabled']).toHaveBeenCalledWith('u-mehmet', false);
    expect(rowOf(mehmet)!.textContent).toContain('Disabled');

    await click(button(rowOf(off), 'Enable'));
    expect(accounts['setTeamMemberEnabled']).toHaveBeenCalledWith('u-selin', true);
  });

  it('takes a member out of the team only after confirming', async () => {
    vi.mocked(window.confirm).mockReturnValueOnce(false);
    await click(button(rowOf(ayse), 'Remove from team'));
    expect(accounts['removeTeamMember']).not.toHaveBeenCalled();

    await click(button(rowOf(ayse), 'Remove from team'));

    expect(accounts['removeTeamMember']).toHaveBeenCalledWith('u-ayse');
    expect(rowOf(ayse)).toBeUndefined();
  });

  it('reloads the team when a member is no longer in it', async () => {
    accounts['removeTeamMember'].mockReturnValue(
      throwError(() => new ApiError(404, 'Not Found', "Account 'u-ayse' was not found.")),
    );
    accounts['getTeam'].mockReturnValue(of([mehmet, off]));

    await click(button(rowOf(ayse), 'Remove from team'));

    expect(accounts['getTeam']).toHaveBeenCalledTimes(2);
    expect(rowOf(ayse)).toBeUndefined();
    // The interceptor already raised a toast for the 404.
    expect(notifications.error).not.toHaveBeenCalled();
  });
});
