import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { account } from '../../../testing/account-fixtures';
import { UserAccount } from '../../core/models/account.model';
import { ApiError } from '../../core/models/api-error.model';
import { AccountService } from '../../core/services/account.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { Users } from './users';

describe('Users', () => {
  const admin = account({
    id: 'u-admin',
    firstName: 'Ada',
    lastName: 'Admin',
    email: 'admin@company.example',
    role: 'Admin',
  });
  const manager = account({
    id: 'u-zeynep',
    firstName: 'Zeynep',
    lastName: 'Kaya',
    email: 'zeynep@company.example',
    role: 'Manager',
    teamSize: 1,
  });
  const member = account({ managerId: 'u-zeynep', managerName: 'Zeynep Kaya' });
  const waiting = account({
    id: 'u-yusuf',
    firstName: 'Yusuf',
    lastName: 'Er',
    email: 'yusuf@company.example',
    role: 'Manager',
    status: 'PendingApproval',
  });
  const disabled = account({
    id: 'u-burak',
    firstName: 'Burak',
    lastName: 'Can',
    email: 'burak@company.example',
    status: 'Disabled',
  });

  let accounts: Record<string, ReturnType<typeof vi.fn>>;
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Users>;
  let element: HTMLElement;

  beforeEach(async () => {
    accounts = {
      getAccounts: vi.fn(() => of([member, admin, disabled, manager, waiting])),
      approve: vi.fn((id: string) => of({ ...waiting, id, status: 'Active' })),
      reject: vi.fn(() => of(undefined)),
      changeRole: vi.fn((id: string, role: string) =>
        of({ ...member, id, role, managerId: null, managerName: null }),
      ),
      assignManager: vi.fn(() => of({ ...member, managerId: null, managerName: null })),
      setEnabled: vi.fn((id: string, enabled: boolean) =>
        of({ ...member, id, status: enabled ? 'Active' : 'Disabled' }),
      ),
      setTemporaryPassword: vi.fn((id: string) =>
        of({
          ...[member, admin, disabled, manager, waiting].find((a) => a.id === id)!,
          mustChangePassword: true,
        }),
      ),
      createAccount: vi.fn(() =>
        of(
          account({
            id: 'u-elif',
            firstName: 'Elif',
            lastName: 'Şahin',
            email: 'elif@company.example',
          }),
        ),
      ),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await TestBed.configureTestingModule({
      imports: [Users],
      providers: [
        { provide: AccountService, useValue: accounts },
        { provide: NotificationService, useValue: notifications },
        { provide: AuthService, useValue: { currentUser: signal({ id: 'u-admin' }) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Users);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => vi.restoreAllMocks());

  const rows = () => Array.from(element.querySelectorAll<HTMLTableRowElement>('tbody tr'));
  const rowOf = (who: UserAccount) => rows().find((r) => r.textContent!.includes(who.email))!;
  const names = () =>
    rows().map((r) => r.querySelector('.name')!.textContent!.replace(/\s+/g, ' ').trim());
  const button = (row: HTMLElement, label: string) =>
    Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent!.trim() === label,
    );

  async function click(target: HTMLElement | undefined): Promise<void> {
    target!.click();
    await fixture.whenStable();
  }

  async function choose(select: HTMLSelectElement, value: string): Promise<void> {
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('lists sign-ups waiting for approval first, then everyone by name', () => {
    expect(names()).toEqual([
      'Yusuf Er',
      'Ada Admin (you)',
      'Burak Can',
      'Mehmet Demir',
      'Zeynep Kaya',
    ]);
    expect(rowOf(manager).textContent).toContain('1 member');
    expect(element.querySelector('.count-badge')!.textContent!.trim()).toBe('1');
  });

  it('filters by status, role and search', async () => {
    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('.tab')).find((t) =>
        t.textContent!.includes('Disabled'),
      ),
    );
    expect(names()).toEqual(['Burak Can']);

    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('.tab')).find(
        (t) => t.textContent!.trim() === 'All',
      ),
    );
    await choose(element.querySelector<HTMLSelectElement>('#role-filter')!, 'Manager');
    expect(names()).toEqual(['Yusuf Er', 'Zeynep Kaya']);

    const search = element.querySelector<HTMLInputElement>('#search')!;
    search.value = 'KAYA';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(names()).toEqual(['Zeynep Kaya']);
  });

  it('finds a Turkish name however its capitals and accents are typed', async () => {
    const ilker = account({
      id: 'u-ilker',
      firstName: 'İlker',
      lastName: 'Işık',
      email: 'u7@company.example',
    });
    accounts['getAccounts'].mockReturnValue(of([member, ilker]));
    fixture = TestBed.createComponent(Users);
    element = fixture.nativeElement;
    await fixture.whenStable();

    const search = element.querySelector<HTMLInputElement>('#search')!;
    for (const term of ['ilker', 'ışık', 'isik']) {
      search.value = term;
      search.dispatchEvent(new Event('input'));
      await fixture.whenStable();
      expect(names()).toEqual(['İlker Işık']);
    }
  });

  it('offers approve and reject for a waiting sign-up, and nothing else', () => {
    const row = rowOf(waiting);

    expect(button(row, 'Approve')).toBeDefined();
    expect(button(row, 'Reject')).toBeDefined();
    expect(button(row, 'Disable')).toBeUndefined();
    expect(row.querySelector('select')).toBeNull();
  });

  it('approves a sign-up and shows it as active', async () => {
    await click(button(rowOf(waiting), 'Approve'));

    expect(accounts['approve']).toHaveBeenCalledWith('u-yusuf');
    expect(rowOf(waiting).textContent).toContain('Active');
    expect(notifications.success).toHaveBeenCalledWith(
      "Yusuf Er can sign in now. They've been emailed.",
    );
  });

  it('rejects a sign-up only after confirming, and drops it from the list', async () => {
    vi.mocked(window.confirm).mockReturnValueOnce(false);
    await click(button(rowOf(waiting), 'Reject'));
    expect(accounts['reject']).not.toHaveBeenCalled();

    await click(button(rowOf(waiting), 'Reject'));
    expect(accounts['reject']).toHaveBeenCalledWith('u-yusuf');
    expect(rowOf(waiting)).toBeUndefined();
  });

  it('never offers to change or switch off your own account', () => {
    const row = rowOf(admin);

    expect(row.querySelector('select')).toBeNull();
    expect(button(row, 'Disable')).toBeUndefined();
    expect(button(row, 'Set password')).toBeUndefined();
  });

  it('changes a role after confirming, and puts the select back when not confirmed', async () => {
    const select = rowOf(member).querySelectorAll<HTMLSelectElement>('select')[0];

    vi.mocked(window.confirm).mockReturnValueOnce(false);
    await choose(select, 'Manager');
    expect(accounts['changeRole']).not.toHaveBeenCalled();
    expect(select.value).toBe('Employee');

    await choose(rowOf(member).querySelectorAll<HTMLSelectElement>('select')[0], 'Manager');
    expect(accounts['changeRole']).toHaveBeenCalledWith('u-mehmet', 'Manager');
  });

  it('shows a refusal in the API’s words and puts the role back', async () => {
    accounts['changeRole'].mockReturnValue(
      throwError(
        () =>
          new ApiError(
            409,
            'Conflict',
            'Zeynep Kaya still has 1 team member(s). Move them to another manager or remove them from the team first.',
          ),
      ),
    );
    const select = rowOf(manager).querySelectorAll<HTMLSelectElement>('select')[0];

    await choose(select, 'Employee');

    expect(notifications.error).toHaveBeenCalledWith(
      expect.stringContaining('still has 1 team member(s)'),
    );
    expect(select.value).toBe('Manager');
  });

  it('moves an employee between teams, offering only active managers', async () => {
    const teamSelect = rowOf(member).querySelectorAll<HTMLSelectElement>('select')[1];
    const options = Array.from(teamSelect.options).map((o) => o.textContent!.trim());
    expect(options).toEqual(['No team', 'Zeynep Kaya']);
    expect(teamSelect.value).toBe('u-zeynep');

    await choose(teamSelect, '');

    expect(accounts['assignManager']).toHaveBeenCalledWith('u-mehmet', null);
  });

  it('disables after confirming, and enables straight away', async () => {
    await click(button(rowOf(member), 'Disable'));
    expect(accounts['setEnabled']).toHaveBeenCalledWith('u-mehmet', false);

    await click(button(rowOf(disabled), 'Enable'));
    expect(accounts['setEnabled']).toHaveBeenCalledWith('u-burak', true);
    expect(window.confirm).toHaveBeenCalledTimes(1);
  });

  it('sets a temporary password that meets the rules, and marks the account', async () => {
    await click(button(rowOf(manager), 'Set password'));
    const input = () =>
      element.querySelector<HTMLInputElement>('app-temporary-password-form input')!;
    const set = () =>
      Array.from(
        element.querySelectorAll<HTMLButtonElement>('app-temporary-password-form button'),
      ).find((b) => b.textContent!.trim() === 'Set')!;

    input().value = 'short';
    input().dispatchEvent(new Event('input'));
    await click(set());
    expect(accounts['setTemporaryPassword']).not.toHaveBeenCalled();
    expect(element.querySelector('app-temporary-password-form')!.textContent).toContain('Add ');

    input().value = 'Temp-Pass-2026';
    input().dispatchEvent(new Event('input'));
    await click(set());

    expect(accounts['setTemporaryPassword']).toHaveBeenCalledWith('u-zeynep', 'Temp-Pass-2026');
    expect(element.querySelector('app-temporary-password-form')).toBeNull();
    expect(rowOf(manager).textContent).toContain('Temporary password');
  });

  it('shows the API’s reason for refusing a temporary password, and keeps the form open', async () => {
    accounts['setTemporaryPassword'].mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            Password: ['Passwords must use at least 1 different characters.'],
          }),
      ),
    );
    await click(button(rowOf(member), 'Set password'));
    const input = element.querySelector<HTMLInputElement>('app-temporary-password-form input')!;
    input.value = 'Temp-Pass-2026';
    input.dispatchEvent(new Event('input'));
    element.querySelector('app-temporary-password-form form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(element.querySelector('app-temporary-password-form')!.textContent).toContain(
      'Passwords must use at least 1 different characters.',
    );
  });

  it('adds an account, with a team only for an employee', async () => {
    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
        (b) => b.textContent!.trim() === 'Add account',
      ),
    );
    const set = async (selector: string, value: string) => {
      const input = element.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
      await fixture.whenStable();
    };
    await set('#new-first-name', ' Elif ');
    await set('#new-last-name', 'Şahin');
    await set('#new-email', 'elif@company.example');
    await set('#new-password', 'Temp-Pass-2026');
    await set('#new-manager', 'u-zeynep');
    await set('#new-role', 'Admin');
    expect(element.querySelector('#new-manager')).toBeNull();

    element.querySelector<HTMLFormElement>('.add-form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(accounts['createAccount']).toHaveBeenCalledWith({
      firstName: 'Elif',
      lastName: 'Şahin',
      email: 'elif@company.example',
      role: 'Admin',
      password: 'Temp-Pass-2026',
      managerId: null,
    });
    expect(names()).toContain('Elif Şahin');
    expect(element.querySelector('.add-form')).toBeNull();
  });

  it('keeps the form open with the reason when the email is taken', async () => {
    accounts['createAccount'].mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', 'A user with this email already exists.')),
    );
    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
        (b) => b.textContent!.trim() === 'Add account',
      ),
    );
    for (const [selector, value] of [
      ['#new-first-name', 'Ada'],
      ['#new-last-name', 'Again'],
      ['#new-email', 'admin@company.example'],
      ['#new-password', 'Temp-Pass-2026'],
    ]) {
      const input = element.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    element.querySelector<HTMLFormElement>('.add-form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(element.querySelector('.add-form [role="alert"]')!.textContent).toContain(
      'A user with this email already exists.',
    );
  });

  it('generates a temporary password for a new account that meets the rules', async () => {
    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find(
        (b) => b.textContent!.trim() === 'Add account',
      ),
    );
    await click(
      Array.from(element.querySelectorAll<HTMLButtonElement>('.add-form button')).find(
        (b) => b.textContent!.trim() === 'Generate',
      ),
    );

    const password = element.querySelector<HTMLInputElement>('#new-password')!.value;
    expect(password).toMatch(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{12}$/);
    expect(element.querySelector('#new-password-error')).toBeNull();
  });
});
