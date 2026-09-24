import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { fromDateTimeLocal } from '../utils/date-time';

export interface DateRangeOptions {
  /** Names of the two datetime-local controls in the group. */
  startKey: string;
  endKey: string;
  /**
   * Longest allowed range, in days of 24 hours of elapsed time - the way the API measures
   * it (UTC difference). Across a daylight-saving change that can differ from calendar days.
   */
  maxDays?: number;
  /** Reject a start that is already in the past. */
  futureOnly?: boolean;
  /** Clock, for tests. */
  now?: () => number;
}

/**
 * Group validator for a start/end pair of datetime-local controls. Mirrors the API's rules
 * for fast feedback - the API still re-checks everything. Errors are set on the group:
 * `rangeOrder` (end not after start), `rangeTooLong`, `startInPast`. Missing or malformed
 * values are left to each control's own `required` validator.
 */
export function dateRangeValidator(options: DateRangeOptions): ValidatorFn {
  const now = options.now ?? Date.now;

  return (group: AbstractControl): ValidationErrors | null => {
    const start = fromDateTimeLocal(group.get(options.startKey)?.value);
    const end = fromDateTimeLocal(group.get(options.endKey)?.value);
    if (!start || !end) {
      return null;
    }

    const errors: ValidationErrors = {};
    if (options.futureOnly && start.getTime() < now()) {
      errors['startInPast'] = true;
    }
    if (end.getTime() <= start.getTime()) {
      errors['rangeOrder'] = true;
    } else if (
      options.maxDays !== undefined &&
      end.getTime() - start.getTime() > options.maxDays * 24 * 60 * 60 * 1000
    ) {
      errors['rangeTooLong'] = { maxDays: options.maxDays };
    }

    return Object.keys(errors).length ? errors : null;
  };
}
