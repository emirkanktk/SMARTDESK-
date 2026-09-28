// Mirrors SmartDesk.Domain.Constants.Roles on the backend.
export const Roles = {
  Admin: 'Admin',
  // Runs a team: adds, switches off and removes their own members (ADR-0020).
  Manager: 'Manager',
  Employee: 'Employee',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];
