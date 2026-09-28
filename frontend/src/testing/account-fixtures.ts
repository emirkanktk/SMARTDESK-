import { UserAccount } from '../app/core/models/account.model';

/** A UserAccount as the API returns it, with sensible defaults. */
export function account(overrides: Partial<UserAccount> = {}): UserAccount {
  const firstName = overrides.firstName ?? 'Mehmet';
  const lastName = overrides.lastName ?? 'Demir';
  return {
    id: 'u-mehmet',
    email: 'mehmet@company.example',
    role: 'Employee',
    status: 'Active',
    managerId: null,
    managerName: null,
    createdAt: '2026-09-01T08:00:00Z',
    teamSize: 0,
    mustChangePassword: false,
    ...overrides,
    firstName,
    lastName,
    displayName: overrides.displayName ?? `${firstName} ${lastName}`,
  };
}
