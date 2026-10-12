// Shapes of the floor plan endpoints - see docs/API_CONTRACTS.md, Floor plans (ADR-0041).

/**
 * A floor: the location its resources share, how many of them the caller may use and how many
 * of those are placed on its plan, and the plan's size when an Admin has uploaded one.
 */
export interface Floor {
  location: string;
  resources: number;
  placed: number;
  plan: FloorPlanImage | null;
}

/** In pixels, so the floor is laid out before the picture loads. */
export interface FloorPlanImage {
  width: number;
  height: number;
  updatedAt: string;
}

/**
 * A plan's picture is made smaller before it's uploaded, and written again as WebP, like a
 * photo, but kept larger so a detailed drawing stays readable: the API takes up to 4096.
 */
export const FLOOR_PLAN = {
  maxSide: 2400,
} as const;
