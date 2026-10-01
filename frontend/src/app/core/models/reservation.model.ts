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
   * It has to be checked in at the door with the resource's QR code, or it's released: its
   * resource asked for that when it was booked, and still does (ADR-0030).
   */
  checkInRequired: boolean;
  /** When the owner checked in; null until then. */
  checkedInAt: string | null;
  /** When it was released because nobody checked in; its status is then Cancelled. */
  releasedAt: string | null;
  /**
   * Who booked it: "First Last", or the email if both are blank. Null when the API can't
   * resolve the user any more, e.g. a deleted account.
   */
  userDisplayName: string | null;
  /**
   * Who the owner invited to it, sorted by name (ADR-0032); empty for most bookings. Each of
   * them may check it in at the door too.
   */
  attendees: ReservationAttendee[];
}

export const ATTENDEE_RESPONSES = ['Pending', 'Accepted', 'Declined'] as const;
/**
 * What someone invited answered: nothing yet (or not since the meeting moved to a new start
 * or another room), coming, or not.
 */
export type AttendeeResponse = (typeof ATTENDEE_RESPONSES)[number];

/** Someone the owner invited to a booking (ADR-0032). */
export interface ReservationAttendee {
  id: string;
  /** "First Last", or the email; null when the API can't resolve the account any more. */
  name: string | null;
  /** Why someone can't come isn't here: that's in the organizer's inbox only. */
  response: AttendeeResponse;
}

/** Body of `POST /api/reservations/{id}/response`: coming, or not and why. */
export interface RespondRequest {
  response: Exclude<AttendeeResponse, 'Pending'>;
  /** Needed to decline, for the organizer; left out when coming. */
  reason?: string;
}

/** Longest reason for not coming the API accepts, as long as a booking's note. */
export const DECLINE_REASON_MAX_LENGTH = 500;

/** Someone the caller may invite, from `GET /api/reservations/invitees` (ADR-0032). */
export interface Invitee {
  id: string;
  name: string;
  email: string;
}

/** Most people one booking may invite; the room's capacity usually stops it sooner. */
export const MAX_ATTENDEES = 50;

/**
 * The check-in window the API enforces (ADR-0030): from `opensBeforeMinutes` before the start
 * until `graceMinutes` after it, and never once the booking is over. A booking nobody checked
 * in to is released when the window closes.
 */
export const CHECK_IN = {
  opensBeforeMinutes: 10,
  graceMinutes: 15,
} as const;

export interface CheckInWindow {
  opensAt: Date;
  closesAt: Date;
}

export function checkInWindow(
  reservation: Pick<Reservation, 'startDateTime' | 'endDateTime'>,
): CheckInWindow {
  const start = new Date(reservation.startDateTime).getTime();
  const end = new Date(reservation.endDateTime).getTime();
  return {
    opensAt: new Date(start - CHECK_IN.opensBeforeMinutes * 60_000),
    closesAt: new Date(Math.min(start + CHECK_IN.graceMinutes * 60_000, end)),
  };
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
  /**
   * Who's invited (ADR-0032): a Manager may invite their own team, an Admin anyone. Nobody
   * when left out.
   */
  attendeeIds?: string[];
}

/**
 * Body of `PUT /api/reservations/{id}` - a full replace; the owner never changes. The list of
 * who's invited is part of it: leaving it out would take everyone off.
 */
export interface UpdateReservationRequest extends CreateReservationRequest {
  attendeeIds: string[];
}

/** How much longer a booking can be made to run with a click, in minutes (ADR-0033). */
export const EXTENSION_OPTIONS = [15, 30, 60] as const;

/** Body of `GET /api/reservations/{id}/extension`: the options that still fit, shortest first. */
export interface ExtensionOptions {
  minutes: number[];
}

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
