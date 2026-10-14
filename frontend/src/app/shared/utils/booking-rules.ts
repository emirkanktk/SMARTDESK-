import { resourceTypeLabel } from '../../core/i18n/labels';
import { OfficeHours, ResourceTypeRules } from '../../core/models/booking-rules.model';
import { WEEK_DAYS, WeekDay } from '../../core/models/reservation.model';
import { formatDuration } from './date-time';

const MINUTE_MS = 60_000;
const DAY_MS = 24 * 60 * MINUTE_MS;

/** Where an instant falls on a time zone's clock. */
export interface ZonedTime {
  /** The local date, as days since 1 January 1970. */
  day: number;
  /** Minutes since local midnight. */
  minutes: number;
  weekDay: WeekDay;
}

const clocks = new Map<string, Intl.DateTimeFormat>();

/** What the clock says in `timeZone` at `date`: the office's day and time, wherever the browser is. */
export function zonedTime(date: Date, timeZone: string): ZonedTime {
  let clock = clocks.get(timeZone);
  if (!clock) {
    clock = new Intl.DateTimeFormat('en-US', {
      timeZone,
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: 'numeric',
      minute: 'numeric',
      hourCycle: 'h23',
    });
    clocks.set(timeZone, clock);
  }
  const parts = clock.formatToParts(date);
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    Number(parts.find((p) => p.type === type)?.value);
  const day = Date.UTC(part('year'), part('month') - 1, part('day')) / DAY_MS;
  return {
    day,
    minutes: part('hour') * 60 + part('minute'),
    weekDay: WEEK_DAYS[(new Date(day * DAY_MS).getUTCDay() + 6) % 7],
  };
}

/** "09:00:00" or "09:00", as minutes since midnight. */
export function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':');
  return Number(hours) * 60 + Number(minutes);
}

/** Whether [start, end) lies within the office's hours, on one workday. */
export function withinOfficeHours(office: OfficeHours, start: Date, end: Date): boolean {
  const from = zonedTime(start, office.timeZone);
  const to = zonedTime(end, office.timeZone);
  return (
    office.workdays.includes(from.weekDay) &&
    from.minutes >= minutesOf(office.opensAt) &&
    to.day === from.day &&
    to.minutes <= minutesOf(office.closesAt)
  );
}

/** "9 h", "1 day 4 h", "7 days". */
export function describeMinutes(minutes: number): string {
  return formatDuration(new Date(0), new Date(minutes * MINUTE_MS));
}

/** What the kind's rules say against booking [start, end), by field; null where they allow it. */
export interface RuleProblems {
  start: string | null;
  end: string | null;
}

/**
 * The kind's own rules (ADR-0046) applied to [start, end), as the API applies them: how long,
 * how many days ahead on the office's calendar, and within office hours. The limit per person
 * depends on bookings the form doesn't have; the API says when it's reached.
 */
export function ruleProblems(
  kind: ResourceTypeRules,
  office: OfficeHours,
  start: Date,
  end: Date,
  now: Date,
): RuleProblems {
  const name = resourceTypeLabel(kind.name);
  let startProblem: string | null = null;
  let endProblem: string | null = null;

  const minutes = kind.maxDurationMinutes;
  if (minutes !== null && end.getTime() - start.getTime() > minutes * MINUTE_MS) {
    const longest = describeMinutes(minutes);
    endProblem = $localize`:@@rules.tooLong:${name}:kind: bookings can last at most ${longest}:duration:.`;
  }

  const days = kind.maxDaysAhead;
  if (
    days !== null &&
    zonedTime(start, office.timeZone).day - zonedTime(now, office.timeZone).day > days
  ) {
    const ahead = describeMinutes(days * 24 * 60);
    startProblem =
      days === 0
        ? $localize`:@@rules.todayOnly:${name}:kind: bookings can only be made for today.`
        : $localize`:@@rules.tooFarAhead:${name}:kind: bookings can start at most ${ahead}:days: ahead.`;
  }

  if (kind.officeHoursOnly && !withinOfficeHours(office, start, end)) {
    startProblem ??= $localize`:@@rules.outsideOfficeHours:${name}:kind: bookings must be within office hours: ${office.description}:hours:.`;
  }

  return { start: startProblem, end: endProblem };
}

/** The kind's own rules, a few words each, for the booking form; empty when it has none. */
export function describeRules(kind: ResourceTypeRules, office: OfficeHours): string[] {
  const rules: string[] = [];
  if (kind.officeHoursOnly) {
    rules.push(
      $localize`:@@rules.hintOfficeHours:within office hours (${office.description}:hours:)`,
    );
  }
  if (kind.maxDurationMinutes !== null) {
    const longest = describeMinutes(kind.maxDurationMinutes);
    rules.push($localize`:@@rules.hintDuration:at most ${longest}:duration: long`);
  }
  if (kind.maxDaysAhead !== null) {
    const ahead = describeMinutes(kind.maxDaysAhead * 24 * 60);
    rules.push(
      kind.maxDaysAhead === 0
        ? $localize`:@@rules.hintToday:for today only`
        : $localize`:@@rules.hintAhead:up to ${ahead}:days: ahead`,
    );
  }
  if (kind.maxActivePerUser !== null) {
    const count = kind.maxActivePerUser;
    rules.push($localize`:@@rules.hintPerPerson:at most ${count}:count: at a time per person`);
  }
  return rules;
}
