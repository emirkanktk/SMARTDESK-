import {
  addDays,
  addMinutes,
  addMonths,
  floorToQuarterHour,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseDateParam,
  parseIso,
  parseMonthParam,
  startOfDay,
  startOfMonth,
  startOfWeek,
  toDateParam,
  toDateTimeLocal,
  toMonthParam,
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

  it('rounds down to the quarter hour', () => {
    expect(toDateTimeLocal(floorToQuarterHour(new Date(2026, 9, 5, 10, 7)))).toBe(
      '2026-10-05T10:00',
    );
    expect(toDateTimeLocal(floorToQuarterHour(new Date(2026, 9, 5, 10, 15)))).toBe(
      '2026-10-05T10:15',
    );
    expect(toDateTimeLocal(floorToQuarterHour(new Date(2026, 9, 5, 10, 59, 59)))).toBe(
      '2026-10-05T10:45',
    );
  });

  it('adds calendar days, keeping the wall-clock time', () => {
    expect(toDateTimeLocal(addDays(new Date(2026, 9, 5, 9, 30), 7))).toBe('2026-10-12T09:30');
    expect(toDateTimeLocal(addDays(new Date(2026, 9, 5, 9, 30), -5))).toBe('2026-09-30T09:30');
    // Across the end of daylight saving: 25 Oct in Europe, 1 Nov in the US.
    expect(toDateTimeLocal(addDays(new Date(2026, 9, 24, 9, 30), 7))).toBe('2026-10-31T09:30');
    expect(toDateTimeLocal(addDays(new Date(2026, 9, 31, 9, 30), 2))).toBe('2026-11-02T09:30');
  });

  it('finds local midnight of a day', () => {
    expect(toDateTimeLocal(startOfDay(new Date(2026, 9, 5, 23, 59)))).toBe('2026-10-05T00:00');
  });

  it('finds the Monday that starts the week', () => {
    expect(toDateTimeLocal(startOfWeek(new Date(2026, 9, 5, 10, 7)))).toBe('2026-10-05T00:00'); // Mon
    expect(toDateTimeLocal(startOfWeek(new Date(2026, 9, 7, 18, 0)))).toBe('2026-10-05T00:00'); // Wed
    expect(toDateTimeLocal(startOfWeek(new Date(2026, 9, 11, 23, 59)))).toBe('2026-10-05T00:00'); // Sun
  });

  it('finds the first of the month, and whole months later or earlier', () => {
    expect(toDateTimeLocal(startOfMonth(new Date(2026, 9, 31, 23, 59)))).toBe('2026-10-01T00:00');
    expect(toDateTimeLocal(addMonths(new Date(2026, 0, 31), 1))).toBe('2026-02-01T00:00');
    expect(toDateTimeLocal(addMonths(new Date(2026, 11, 15), 1))).toBe('2027-01-01T00:00');
    expect(toDateTimeLocal(addMonths(new Date(2026, 0, 15), -1))).toBe('2025-12-01T00:00');
  });

  it('round-trips a month through a query parameter and rejects bad ones', () => {
    expect(toMonthParam(new Date(2026, 9, 31))).toBe('2026-10');
    expect(parseMonthParam('2026-10')?.getTime()).toBe(new Date(2026, 9, 1).getTime());
    expect(parseMonthParam('2026-13')).toBeNull();
    expect(parseMonthParam('2026-00')).toBeNull();
    expect(parseMonthParam('2026-10-05')).toBeNull();
    expect(parseMonthParam(undefined)).toBeNull();
  });

  it('round-trips a date through a query parameter and rejects bad ones', () => {
    expect(toDateParam(new Date(2026, 9, 5, 15, 0))).toBe('2026-10-05');
    expect(parseDateParam('2026-10-05')?.getTime()).toBe(new Date(2026, 9, 5).getTime());
    expect(parseDateParam('2026-02-31')).toBeNull();
    expect(parseDateParam('5.10.2026')).toBeNull();
    expect(parseDateParam(undefined)).toBeNull();
  });

  it('formats durations', () => {
    const start = new Date(2026, 9, 5, 10, 0);
    expect(formatDuration(start, addMinutes(start, 45))).toBe('45 min');
    expect(formatDuration(start, addMinutes(start, 60))).toBe('1 h');
    expect(formatDuration(start, addMinutes(start, 90))).toBe('1 h 30 min');
    expect(formatDuration(start, addMinutes(start, 3 * 24 * 60 + 120))).toBe('3 days 2 h');
  });
});
