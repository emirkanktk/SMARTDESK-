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
