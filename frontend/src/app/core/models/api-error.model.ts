import { HttpErrorResponse } from '@angular/common/http';

/** For a request that got no answer at all. */
export const SERVER_UNREACHABLE = $localize`:@@error.unreachable:Cannot reach the server. Check your connection and try again.`;

/**
 * Normalized shape of a failed API call. The API answers errors with RFC 7807
 * ProblemDetails; validation failures (400) add an `errors` map of field -> messages.
 */
export class ApiError extends Error {
  constructor(
    /** HTTP status, or 0 when the server could not be reached. */
    readonly status: number,
    readonly title: string,
    readonly detail: string | null = null,
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
    /** A machine-readable reason some ProblemDetails carry, e.g. "password-change-required". */
    readonly code: string | null = null,
  ) {
    super(detail ?? title);
    this.name = 'ApiError';
  }

  static fromHttpError(error: HttpErrorResponse): ApiError {
    if (error.status === 0) {
      return new ApiError(0, SERVER_UNREACHABLE);
    }

    const body: unknown = error.error;
    if (typeof body !== 'object' || body === null) {
      return new ApiError(
        error.status,
        error.statusText || $localize`:@@error.requestFailed:Request failed.`,
      );
    }

    const problem = body as { title?: unknown; detail?: unknown; errors?: unknown; code?: unknown };
    return new ApiError(
      error.status,
      typeof problem.title === 'string'
        ? problem.title
        : error.statusText || $localize`:@@error.requestFailed:Request failed.`,
      typeof problem.detail === 'string' ? problem.detail : null,
      isFieldErrorMap(problem.errors) ? problem.errors : {},
      typeof problem.code === 'string' ? problem.code : null,
    );
  }
}

function isFieldErrorMap(value: unknown): value is Record<string, string[]> {
  return (
    typeof value === 'object' &&
    value !== null &&
    Object.values(value).every((v) => Array.isArray(v) && v.every((m) => typeof m === 'string'))
  );
}
