// Helpers for <input type="datetime-local">, whose value is local wall-clock time without an
// offset ("2026-10-05T14:30"). The API speaks UTC, so values are converted at the edges:
// fromDateTimeLocal() -> Date -> toISOString() on the way out, and the reverse on the way in.

const MINUTE_MS = 60_000;

/** Formats a Date as a datetime-local value in the browser's time zone. */
export function toDateTimeLocal(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/** Parses a datetime-local value (local time); null when empty or malformed. */
export function fromDateTimeLocal(value: string | null | undefined): Date | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) {
    return null;
  }
  // A date-time string without an offset is parsed as local time.
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Parses an ISO-8601 timestamp (e.g. from a query parameter); null when invalid. */
export function parseIso(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * The next full or half hour after `now` on the local clock, e.g. 10:07 -> 10:30,
 * 10:30 -> 11:00. Rounded on the wall clock, not on epoch time: in a zone offset by 45
 * minutes (Nepal, UTC+05:45) epoch half hours fall on :15 and :45.
 */
export function nextHalfHour(now: Date = new Date()): Date {
  const next = new Date(now);
  next.setSeconds(0, 0);
  next.setMinutes(next.getMinutes() < 30 ? 30 : 60);
  return next;
}

/**
 * `date` rounded down to the quarter hour, e.g. 10:07 -> 10:00. Use it for the `min` of a
 * picker with step="900": the browser counts steps from `min`, so an off-grid min (10:07)
 * shifts every allowed value (10:22, 10:37...) and flags on-grid ones as a step mismatch.
 */
export function floorToQuarterHour(date: Date = new Date()): Date {
  const step = 15 * MINUTE_MS;
  return new Date(Math.floor(date.getTime() / step) * step);
}

export function addMinutes(date: Date, minutes: number): Date {
  return new Date(date.getTime() + minutes * MINUTE_MS);
}

/**
 * The same wall-clock time `days` calendar days later, in local time. Not `days` x 24 h: a
 * daylight-saving change makes one day 23 or 25 hours long.
 */
export function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

/** Local midnight at the start of `date`'s day. */
export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/** Local midnight at the start of the week (Monday) that contains `date`. */
export function startOfWeek(date: Date): Date {
  const result = startOfDay(date);
  const sinceMonday = (result.getDay() + 6) % 7; // getDay(): Sunday = 0
  return addDays(result, -sinceMonday);
}

/** A local date as "2026-10-05", for query strings. */
export function toDateParam(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Local midnight of a "2026-10-05" date; null when empty or malformed. */
export function parseDateParam(value: string | null | undefined): Date | null {
  const match = value?.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!match) {
    return null;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]) - 1, Number(match[3])];
  const date = new Date(year, month, day);
  // Reject dates the Date constructor would roll over, like 2026-02-31.
  return date.getMonth() === month && date.getDate() === day ? date : null;
}

/** "45 min", "1 h", "1 h 30 min", "2 days 3 h". */
export function formatDuration(start: Date, end: Date): string {
  let minutes = Math.max(0, Math.round((end.getTime() - start.getTime()) / MINUTE_MS));
  const days = Math.floor(minutes / (24 * 60));
  minutes -= days * 24 * 60;
  const hours = Math.floor(minutes / 60);
  minutes -= hours * 60;

  const parts: string[] = [];
  if (days) parts.push(days === 1 ? '1 day' : `${days} days`);
  if (hours) parts.push(`${hours} h`);
  if (minutes || parts.length === 0) parts.push(`${minutes} min`);
  return parts.join(' ');
}
