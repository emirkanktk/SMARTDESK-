// Shapes of the resource endpoints - see docs/API_CONTRACTS.md, Resources.

export const RESOURCE_STATUSES = ['Active', 'Inactive', 'Maintenance'] as const;
export type ResourceStatus = (typeof RESOURCE_STATUSES)[number];

export interface ResourceType {
  id: string;
  name: string;
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
}

export interface ResourceFilter {
  resourceTypeId?: string | null;
  status?: ResourceStatus | null;
}

export interface CreateResourceRequest {
  name: string;
  resourceTypeId: string;
  capacity: number;
  location: string;
  description: string | null;
  features: string[];
}

/** Full replace: every field is sent, omitted features are removed. */
export interface UpdateResourceRequest extends CreateResourceRequest {
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
} as const;
