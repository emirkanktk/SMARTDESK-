import { ApiError } from '../../core/models/api-error.model';

/**
 * What to tell the user when an account action fails. The error interceptor already raised a
 * toast for 403, 404, 5xx and an unreachable server, so this returns null for those. A 409
 * carries a message written to be shown as-is ("…still has 2 team member(s)…").
 */
export function accountActionError(error: unknown): string | null {
  if (!(error instanceof ApiError)) {
    return $localize`:@@error.generic:Something went wrong. Please try again.`;
  }
  switch (error.status) {
    case 409:
      return error.detail ?? error.title;
    case 400:
      return Object.values(error.fieldErrors).flat()[0] ?? error.detail ?? error.title;
    default:
      return null;
  }
}
