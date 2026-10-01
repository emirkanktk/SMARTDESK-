// Shapes of the resource endpoints - see docs/API_CONTRACTS.md, Resources.

export const RESOURCE_STATUSES = ['Active', 'Inactive', 'Maintenance'] as const;
export type ResourceStatus = (typeof RESOURCE_STATUSES)[number];

export interface ResourceType {
  id: string;
  name: string;
}

/** A team a resource belongs to: the team's manager (ADR-0022). */
export interface ResourceTeam {
  id: string;
  managerName: string;
}

export interface Resource {
  id: string;
  name: string;
  resourceTypeId: string;
  resourceTypeName: string;
  capacity: number;
  location: string;
  status: ResourceStatus;
  description: string | null;
  features: string[];
  /** Empty: shared by everyone. Otherwise only these teams (and admins) can use it. */
  teams: ResourceTeam[];
  /** When its photo last changed; null when it has none and its type's picture shows (ADR-0027). */
  photoUpdatedAt: string | null;
  /** Bookings of it must be checked in at the door with its QR code, or they're released (ADR-0030). */
  checkInRequired: boolean;
}

export interface ResourceFilter {
  resourceTypeId?: string | null;
  status?: ResourceStatus | null;
}

/** Query for `GET /api/resources/available`. Times are UTC ISO-8601 strings. */
export interface AvailabilityQuery {
  startDateTime: string;
  endDateTime: string;
  resourceTypeId?: string | null;
  minCapacity?: number | null;
}

/** Longest window `GET /api/resources/available` accepts. */
export const AVAILABILITY_MAX_DAYS = 31;

interface ResourceFields {
  name: string;
  resourceTypeId: string;
  capacity: number;
  location: string;
  description: string | null;
  features: string[];
  checkInRequired: boolean;
}

/**
 * `teamIds`: an admin's choice, none meaning shared by everyone. A manager's new resource always
 * belongs to their own team, so they leave it out (ADR-0022).
 */
export interface CreateResourceRequest extends ResourceFields {
  teamIds?: string[];
}

/** Full replace: every field is sent, omitted features are removed. Teams don't change here. */
export interface UpdateResourceRequest extends ResourceFields {
  status: ResourceStatus;
}

// Validation limits enforced by the API; the forms mirror them for fast feedback.
export const RESOURCE_LIMITS = {
  nameMaxLength: 200,
  locationMaxLength: 200,
  descriptionMaxLength: 1000,
  capacityMin: 1,
  capacityMax: 1000,
  maxFeatures: 20,
  featureMaxLength: 100,
  maxTeams: 20,
} as const;

/**
 * The API takes a JPEG, PNG or WebP of at most 2 MB. The app makes a larger picture smaller
 * before uploading it, so the longest side is at most `maxSide` pixels.
 */
export const RESOURCE_PHOTO = {
  types: ['image/jpeg', 'image/png', 'image/webp'],
  maxBytes: 2 * 1024 * 1024,
  maxSide: 1600,
} as const;

/**
 * What a resource's QR code holds (ADR-0030): its secret, and the check-in page's address with
 * the secret in it. Only those who may edit the resource get it.
 */
export interface CheckInCode {
  code: string;
  url: string;
  /**
   * When a screen was set up at its door (ADR-0034). While there is one, only the screen's
   * changing code checks in, and the printed one doesn't.
   */
  doorScreenSince: string | null;
}

/** A door screen's address, with its token after '#': shown once, when it's set up. */
export interface DoorScreenLink {
  url: string;
  setUpAt: string;
}
