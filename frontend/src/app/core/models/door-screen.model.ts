import { ResourceStatus } from './resource.model';

// Shape of `GET /api/door/{resourceId}` - see docs/API_CONTRACTS.md, Door screen (ADR-0034).

/** A booking of today that isn't over. A screen in the corridor shows no names. */
export interface DoorBooking {
  startDateTime: string;
  endDateTime: string;
  /** It has to be checked in at the door. */
  checkInNeeded: boolean;
  checkedIn: boolean;
}

export interface DoorBlock {
  startDateTime: string;
  endDateTime: string;
  reason: string;
}

/** What a resource's door screen shows, asked with the screen's own token. */
export interface DoorScreen {
  resourceName: string;
  location: string;
  capacity: number;
  status: ResourceStatus;
  /** The server's clock, so the screen's own can be off without showing wrong states. */
  now: string;
  bookings: DoorBooking[];
  blocks: DoorBlock[];
  /** The check-in link for the QR code, until `changesAt`; null when it uses no check-in. */
  checkIn: { url: string; changesAt: string } | null;
}

/** The header the screen sends its token in, so it stays out of addresses and logs. */
export const DOOR_TOKEN_HEADER = 'X-Door-Token';
