// Shapes of the resource block endpoints - see docs/API_CONTRACTS.md, Resource blocks.

/** A window in which a resource can't be booked. Times are UTC ISO-8601 strings. */
export interface ResourceBlock {
  id: string;
  resourceId: string;
  resourceName: string;
  startDateTime: string;
  endDateTime: string;
  reason: string;
  createdByUserId: string;
}

/** Query for `GET /api/resource-blocks`: blocks overlapping [from, to), UTC ISO strings. */
export interface ResourceBlockQuery {
  from: string;
  to: string;
  resourceId?: string | null;
}

export interface CreateResourceBlockRequest {
  resourceId: string;
  startDateTime: string;
  endDateTime: string;
  reason: string;
  /**
   * The Confirmed bookings in the window are cancelled with it, and their people told why
   * (ADR-0038). Without it, they're a 409 to deal with first.
   */
  cancelReservations?: boolean;
}

// Validation limits enforced by the API; the form mirrors them for fast feedback.
export const RESOURCE_BLOCK_LIMITS = {
  reasonMaxLength: 200,
  /** Longest block, in days of 24 hours of elapsed time. */
  maxDays: 366,
  /** How many minutes in the past a block may still start, as for a reservation. */
  pastStartGraceMinutes: 5,
} as const;
