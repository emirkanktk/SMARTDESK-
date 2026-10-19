// Shapes of the history of changes, GET /api/audit (ADR-0052) - see docs/API_CONTRACTS.md, History.

export const AUDIT_AREAS = ['bookings', 'resources', 'rules', 'problems', 'accounts'] as const;
export type AuditArea = (typeof AUDIT_AREAS)[number];

export type AuditAction = 'Created' | 'Updated' | 'Deleted';

/** A signed-in person; someone without an account (the door page, signing up); or SmartDesk itself. */
export type AuditActor = 'User' | 'Anonymous' | 'System';

/**
 * One column before and after, as text: instants in UTC ISO 8601, enums by name, a secret as
 * "***". `old` is null for a new row, `new` for a deleted one.
 */
export interface AuditChange {
  property: string;
  old: string | null;
  new: string | null;
}

export interface AuditEntry {
  /** In the order they were written; the next page asks for those before the last one shown. */
  id: number;
  at: string;
  actor: AuditActor;
  userId: string | null;
  /** "Reservation", "Resource", "User"...: see AuditEntityTypes in the API. */
  entityType: string;
  entityId: string;
  action: AuditAction;
  resourceId: string | null;
  /** Which row it was, as data: a resource's name, a booking's two UTC instants. */
  label: string | null;
  changes: AuditChange[];
}

export interface AuditPage {
  items: AuditEntry[];
  hasMore: boolean;
  /** The people the entries mention, by id, as they're called now. */
  people: Record<string, string>;
  resources: Record<string, string>;
}

export interface AuditFilter {
  area?: AuditArea | null;
  resourceId?: string | null;
  userId?: string | null;
  /** UTC instants: from inclusive, to exclusive. */
  from?: string | null;
  to?: string | null;
}
