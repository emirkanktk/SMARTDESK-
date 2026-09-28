import { Role } from '../constants/roles';

export const ACCOUNT_STATUSES = ['Active', 'PendingApproval', 'Disabled'] as const;

export type AccountStatus = (typeof ACCOUNT_STATUSES)[number];

/** One account, as `GET /api/users` and `GET /api/team` return it (ADR-0020). */
export interface UserAccount {
  id: string;
  firstName: string;
  lastName: string;
  displayName: string;
  email: string;
  role: Role;
  status: AccountStatus;
  /** Set only for an Employee in a Manager's team (or asking to join one). */
  managerId: string | null;
  managerName: string | null;
  createdAt: string;
  /** How many Employees have this account as their manager, requests to join included. */
  teamSize: number;
  /** A temporary password is still in place (ADR-0021). */
  mustChangePassword: boolean;
}

export interface AccountQuery {
  status?: AccountStatus | null;
  role?: Role | null;
  search?: string | null;
}

/**
 * Body of `POST /api/auth/register`. Without `managerEmail`: a Manager waiting for an Admin.
 * With it: an Employee asking to join that manager's team (ADR-0021).
 */
export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  managerEmail?: string | null;
}

/** Body of `POST /api/users`. `password` is temporary: the owner changes it at first sign-in. */
export interface NewAccountRequest {
  firstName: string;
  lastName: string;
  email: string;
  role: Role;
  password: string;
  managerId: string | null;
}

/** Body of `POST /api/team/members`. `password` is temporary, as for a new account. */
export interface NewTeamMemberRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}
