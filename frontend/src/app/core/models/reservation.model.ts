// Shapes of the reservation endpoints - see docs/API_CONTRACTS.md, Reservations.

export const RESERVATION_STATUSES = ['Confirmed', 'Cancelled'] as const;
export type ReservationStatus = (typeof RESERVATION_STATUSES)[number];

/** Every reservation endpoint returns this shape; it is also the SignalR event payload. */
export interface Reservation {
  id: string;
  resourceId: string;
  resourceName: string;
  resourceTypeName: string;
  resourceLocation: string;
  userId: string;
  /** UTC ISO-8601, e.g. "2026-10-05T10:00:00Z". */
  startDateTime: string;
  endDateTime: string;
  status: ReservationStatus;
  note: string | null;
  /** Who booked it: "First Last", or the email if both are blank. */
  userDisplayName: string;
}

/** Body of `POST /api/reservations`. The owner is always the caller. */
export interface CreateReservationRequest {
  resourceId: string;
  startDateTime: string;
  endDateTime: string;
  note: string | null;
}

/** Body of `PUT /api/reservations/{id}` - a full replace; the owner never changes. */
export type UpdateReservationRequest = CreateReservationRequest;

// Rules enforced by the API; the form mirrors them for fast feedback.
export const RESERVATION_LIMITS = {
  noteMaxLength: 500,
  /** Longest reservation, as 24 h days of elapsed time. */
  maxDays: 31,
  /** How far in the past a new start may be, so "from 14:00" still works at 14:03. */
  pastStartGraceMinutes: 5,
} as const;
