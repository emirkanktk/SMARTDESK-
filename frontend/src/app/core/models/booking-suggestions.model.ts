// The `suggestions` of a 409 for a booking that can't be made (ADR-0042) - see
// docs/API_CONTRACTS.md, Reservations.

import { ApiError } from './api-error.model';

/** What would do instead: other times on the same resource, and other resources then. */
export interface BookingSuggestions {
  times: SuggestedTime[];
  resources: SuggestedResource[];
}

/** As long as the time asked for. UTC ISO-8601 strings. */
export interface SuggestedTime {
  start: string;
  end: string;
}

export interface SuggestedResource {
  id: string;
  name: string;
  capacity: number;
  location: string;
}

/** A 409's suggestions, when it has some of that shape; null otherwise. */
export function bookingSuggestions(error: unknown): BookingSuggestions | null {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return null;
  }
  const value = error.suggestions as { times?: unknown; resources?: unknown } | null;
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const times = Array.isArray(value.times) ? value.times.filter(isTime) : [];
  const resources = Array.isArray(value.resources) ? value.resources.filter(isResource) : [];
  return times.length > 0 || resources.length > 0 ? { times, resources } : null;
}

function isTime(value: unknown): value is SuggestedTime {
  const time = value as Partial<SuggestedTime> | null;
  return (
    typeof time === 'object' &&
    time !== null &&
    typeof time.start === 'string' &&
    typeof time.end === 'string'
  );
}

function isResource(value: unknown): value is SuggestedResource {
  const resource = value as Partial<SuggestedResource> | null;
  return (
    typeof resource === 'object' &&
    resource !== null &&
    typeof resource.id === 'string' &&
    typeof resource.name === 'string' &&
    typeof resource.capacity === 'number' &&
    typeof resource.location === 'string'
  );
}
