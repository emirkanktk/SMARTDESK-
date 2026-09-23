import {
  addMinutes,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseIso,
  toDateTimeLocal,
} from './date-time';

describe('date-time helpers', () => {
  it('round-trips a local date through a datetime-local value', () => {
    const date = new Date(2026, 9, 5, 9, 5); // 5 Oct 2026, 09:05 local

    expect(toDateTimeLocal(date)).toBe('2026-10-05T09:05');
    expect(fromDateTimeLocal('2026-10-05T09:05')?.getTime()).toBe(date.getTime());
  });

  it('rejects empty or malformed datetime-local values', () => {
    expect(fromDateTimeLocal('')).toBeNull();
    expect(fromDateTimeLocal(null)).toBeNull();
    expect(fromDateTimeLocal('05/10/2026 09:00')).toBeNull();
  });

  it('parses ISO timestamps and rejects garbage', () => {
    expect(parseIso('2026-10-05T07:00:00.000Z')?.toISOString()).toBe('2026-10-05T07:00:00.000Z');
    expect(parseIso('not-a-date')).toBeNull();
    expect(parseIso(undefined)).toBeNull();
  });

  it('rounds up to the next half hour', () => {
    expect(toDateTimeLocal(nextHalfHour(new Date(2026, 9, 5, 10, 7)))).toBe('2026-10-05T10:30');
    expect(toDateTimeLocal(nextHalfHour(new Date(2026, 9, 5, 10, 30)))).toBe('2026-10-05T11:00');
    expect(toDateTimeLocal(nextHalfHour(new Date(2026, 9, 5, 23, 45)))).toBe('2026-10-06T00:00');
  });

  it('formats durations', () => {
    const start = new Date(2026, 9, 5, 10, 0);
    expect(formatDuration(start, addMinutes(start, 45))).toBe('45 min');
    expect(formatDuration(start, addMinutes(start, 60))).toBe('1 h');
    expect(formatDuration(start, addMinutes(start, 90))).toBe('1 h 30 min');
    expect(formatDuration(start, addMinutes(start, 3 * 24 * 60 + 120))).toBe('3 days 2 h');
  });
});
