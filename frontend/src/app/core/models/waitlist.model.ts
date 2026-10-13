// Shapes of the waiting list endpoints - see docs/API_CONTRACTS.md, Waiting list (ADR-0043).

/** A time someone waits for on a resource that's taken then. Times are UTC ISO-8601 strings. */
export interface WaitlistEntry {
  id: string;
  resourceId: string;
  resourceName: string;
  resourceLocation: string;
  startDateTime: string;
  endDateTime: string;
  createdAt: string;
  /** Their place in line: 1 when nobody waiting for any of the same time joined earlier. */
  position: number;
}

export interface JoinWaitlistRequest {
  resourceId: string;
  startDateTime: string;
  endDateTime: string;
}
