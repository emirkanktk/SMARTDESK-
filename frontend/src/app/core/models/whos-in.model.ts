/** Who's in the office on a day (ADR-0063), as `GET /api/whos-in` answers. */
export interface WhosIn {
  /** The day, "2026-11-26". */
  date: string;
  timeZone: string;
  /** The user is in a team, a Manager's own or an Employee's: they can see just theirs. */
  hasTeam: boolean;
  /** In the order of the request's language. */
  people: OfficePerson[];
}

/** Someone who's in that day, with what they booked or said they'd come to, earliest first. */
export interface OfficePerson {
  userId: string;
  name: string;
  isMe: boolean;
  /** In the user's team, the user included. */
  inMyTeam: boolean;
  visits: OfficeVisit[];
}

/**
 * One place and time: their own booking, or a meeting someone else booked that they said they'd
 * come to (`invited`), with whose it is - null when that account is gone.
 */
export interface OfficeVisit {
  reservationId: string;
  resourceId: string;
  resourceName: string;
  resourceTypeName: string;
  location: string;
  startDateTime: string;
  endDateTime: string;
  checkedIn: boolean;
  invited: boolean;
  organizerName: string | null;
}
