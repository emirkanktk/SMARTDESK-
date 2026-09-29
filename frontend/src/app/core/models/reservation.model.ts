// Shapes of the reservation endpoints - see docs/API_CONTRACTS.md, Reservations.

export const RESERVATION_STATUSES = ['Confirmed', 'Cancelled'] as const;
export type ReservationStatus = (typeof RESERVATION_STATUSES)[number];

/** Every reservation endpoint returns this shape; it is also the SignalR event payload. */
export interface Reservation {
  id: string;
  resourceId: string;
  resourceName: string;
  resourceTypeId: string;
  resourceTypeName: string;
  resourceLocation: string;
  userId: string;
  /** UTC ISO-8601, e.g. "2026-10-05T10:00:00Z". */
  startDateTime: string;
  endDateTime: string;
  status: ReservationStatus;
  note: string | null;
  /** Remind the owner this many minutes before the start; null means no reminder. */
  reminderMinutes: number | null;
  /**
   * Who booked it: "First Last", or the email if both are blank. Null when the API can't
   * resolve the user any more, e.g. a deleted account.
   */
  userDisplayName: string | null;
}

/**
 * Widest range `GET /api/reservations` and `GET /api/resource-blocks` accept: six weeks, plus
 * an hour, because six local weeks across the autumn clock change are 42 days and an hour of
 * elapsed time. Measured as elapsed time, as the API does.
 */
export const CALENDAR_MAX_DAYS = 42;
export const CALENDAR_MAX_RANGE_MS = (CALENDAR_MAX_DAYS * 24 + 1) * 60 * 60 * 1000;

/** Query for the calendar, `GET /api/reservations`. Times are UTC ISO strings. */
export interface CalendarQuery {
  /** Confirmed reservations overlapping [from, to), at most `CALENDAR_MAX_DAYS` apart. */
  from: string;
  to: string;
  resourceId?: string | null;
  resourceTypeId?: string | null;
}

/** Body of `POST /api/reservations`. The owner is always the caller. */
export interface CreateReservationRequest {
  resourceId: string;
  startDateTime: string;
  endDateTime: string;
  note: string | null;
  /** One of REMINDER_OPTIONS, or null for none. Leaving it out of a PUT turns it off. */
  reminderMinutes: number | null;
}

/** Body of `PUT /api/reservations/{id}` - a full replace; the owner never changes. */
export type UpdateReservationRequest = CreateReservationRequest;

/** Reminder choices the API accepts, in minutes before the start (ADR-0018). */
export const REMINDER_OPTIONS = [5, 15, 30, 60] as const;

/** What a new booking starts with; the owner can change it or turn it off. */
export const DEFAULT_REMINDER_MINUTES = 15;

// Rules enforced by the API; the form mirrors them for fast feedback.
export const RESERVATION_LIMITS = {
  noteMaxLength: 500,
  /** Longest reservation, as 24 h days of elapsed time. */
  maxDays: 31,
  /** How far in the past a new start may be, so "from 14:00" still works at 14:03. */
  pastStartGraceMinutes: 5,
  /** How far ahead a booking, or a search for one, may start. */
  maxAdvanceDays: 180,
} as const;
