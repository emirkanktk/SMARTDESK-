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
  /**
   * With `futureOnly`, how many minutes in the past a start may still be. The API allows 5
   * for bookings, so "the room I'm in, from 14:00" still works at 14:03.
   */
  graceMinutes?: number;
  /** Reject an end that is already in the past (a reservation that is in progress). */
  endInFuture?: boolean;
  /** Clock, for tests. */
  now?: () => number;
}

/**
 * Group validator for a start/end pair of datetime-local controls. Mirrors the API's rules
 * for fast feedback - the API still re-checks everything. Errors are set on the group:
 * `rangeOrder` (end not after start), `rangeTooLong`, `startInPast`, `endInPast`. Missing
 * or malformed values are left to each control's own `required` validator. Disabled
 * controls are read too, so a locked start still counts.
 */
export function dateRangeValidator(options: DateRangeOptions): ValidatorFn {
  const now = options.now ?? Date.now;
  const graceMs = (options.graceMinutes ?? 0) * 60 * 1000;

  return (group: AbstractControl): ValidationErrors | null => {
    const start = fromDateTimeLocal(group.get(options.startKey)?.value);
    const end = fromDateTimeLocal(group.get(options.endKey)?.value);
    if (!start || !end) {
      return null;
    }

    const errors: ValidationErrors = {};
    if (options.futureOnly && start.getTime() < now() - graceMs) {
      errors['startInPast'] = true;
    }
    if (options.endInFuture && end.getTime() <= now()) {
      errors['endInPast'] = true;
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
