// Mirrors SmartDesk.Domain.Constants.Roles on the backend.
export const Roles = {
  Admin: 'Admin',
  Employee: 'Employee',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];
