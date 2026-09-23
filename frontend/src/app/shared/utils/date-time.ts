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

/** The next full or half hour after `now`, e.g. 10:07 -> 10:30, 10:30 -> 11:00. */
export function nextHalfHour(now: Date = new Date()): Date {
  const step = 30 * MINUTE_MS;
  return new Date(Math.floor(now.getTime() / step) * step + step);
}

export function addMinutes(date: Date, minutes: number): Date {
  return new Date(date.getTime() + minutes * MINUTE_MS);
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
