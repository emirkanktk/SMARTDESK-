import { formatDate } from '@angular/common';
import { Recurrence, WEEK_DAYS, WeekDay } from '../../core/models/reservation.model';
import { parseDateParam } from './date-time';

// Saying how a booking repeats (ADR-0035), in the reader's language.

/** Monday to Friday: the days of "every weekday". */
export const WORKING_DAYS: readonly WeekDay[] = WEEK_DAYS.slice(0, 5);

/** The browser's IANA time zone: the one a series' local times are worked out in. */
export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/** The API's name for the day of the week of a local date. */
export function weekDayOf(date: Date): WeekDay {
  return WEEK_DAYS[(date.getDay() + 6) % 7];
}

/** A day's name in the reader's language: "Monday", or "Mon" when `short`. */
export function dayName(day: WeekDay, locale: string, short = false): string {
  // Monday 5 October 2026, and the days after it.
  const date = new Date(2026, 9, 5 + WEEK_DAYS.indexOf(day));
  return formatDate(date, short ? 'EEE' : 'EEEE', locale);
}

/** "every day", "every 3 days", "every weekday", "every Monday and Thursday", "every 2 weeks on Monday". */
export function describeRule(
  rule: Pick<Recurrence, 'frequency' | 'interval' | 'daysOfWeek'>,
  locale: string,
): string {
  if (rule.frequency === 'Daily') {
    return rule.interval === 1
      ? $localize`:@@series.everyDay:every day`
      : $localize`:@@series.everyDays:every ${rule.interval}:count: days`;
  }
  const days = WEEK_DAYS.filter((day) => rule.daysOfWeek.includes(day));
  if (
    rule.interval === 1 &&
    days.length === WORKING_DAYS.length &&
    days.every((d) => WORKING_DAYS.includes(d))
  ) {
    return $localize`:@@series.everyWeekday:every weekday`;
  }
  const names = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' }).format(
    days.map((day) => dayName(day, locale)),
  );
  return rule.interval === 1
    ? $localize`:@@series.everyWeekOn:every ${names}:days:`
    : $localize`:@@series.everyWeeksOn:every ${rule.interval}:count: weeks on ${names}:days:`;
}

/** "every Monday until 2 Nov 2026". */
export function describeSeries(rule: Recurrence, locale: string): string {
  const until = parseDateParam(rule.until);
  const last = until ? formatDate(until, 'd MMM y', locale) : rule.until;
  return $localize`:@@series.until:${describeRule(rule, locale)}:rule: until ${last}:until:`;
}
