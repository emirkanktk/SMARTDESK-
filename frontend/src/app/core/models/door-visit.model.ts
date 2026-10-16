import { DoorBlock, DoorBooking } from './door-screen.model';
import { IssueCategory, IssueStatus } from './issue.model';
import { ResourceStatus } from './resource.model';

// Shapes of the page a QR code opens without an account (ADR-0050) - see docs/API_CONTRACTS.md,
// Door screen.

/** A problem reported there and not fixed yet. Nobody's name, and no photo. */
export interface DoorIssue {
  id: string;
  category: IssueCategory;
  description: string;
  isBlocking: boolean;
  status: IssueStatus;
  createdAt: string;
}

/** What `GET /api/door/{resourceId}/visit` gives whoever opened the resource's QR code. */
export interface DoorVisit {
  resourceName: string;
  resourceTypeName: string;
  location: string;
  capacity: number;
  description: string | null;
  features: string[];
  status: ResourceStatus;
  checkInRequired: boolean;
  /** The server's clock, so the phone's own can be off without showing wrong states. */
  now: string;
  /** The rest of today's bookings, as times only, as its door screen shows them. */
  bookings: DoorBooking[];
  blocks: DoorBlock[];
  /** Those that make it unusable first, then the newest. */
  openIssues: DoorIssue[];
}

/** The header the page sends its code in when it only reads, so it stays out of addresses and logs. */
export const CHECK_IN_CODE_HEADER = 'X-Check-In-Code';
