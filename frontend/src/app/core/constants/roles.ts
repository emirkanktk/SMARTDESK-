// Mirrors SmartDesk.Domain.Constants.Roles on the backend.
export const Roles = {
  Admin: 'Admin',
  // Runs a team: adds, switches off and removes their own members (ADR-0020).
  Manager: 'Manager',
  Employee: 'Employee',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

/** Every role, highest first - the order the API ranks them in. */
export const ALL_ROLES: readonly Role[] = [Roles.Admin, Roles.Manager, Roles.Employee];
