import { Recurrence } from '../../core/models/reservation.model';
import { browserTimeZone, dayName, describeRule, describeSeries, weekDayOf } from './recurrence';

const rule = (overrides: Partial<Recurrence> = {}): Recurrence => ({
  frequency: 'Weekly',
  interval: 1,
  daysOfWeek: ['Monday'],
  until: '2026-11-02',
  timeZone: 'Europe/Istanbul',
  ...overrides,
});

describe('recurrence', () => {
  it("names a date's day of the week as the API does", () => {
    expect(weekDayOf(new Date(2026, 9, 5))).toBe('Monday');
    expect(weekDayOf(new Date(2026, 9, 11))).toBe('Sunday');
  });

  it("says a day's name, long or short", () => {
    expect(dayName('Thursday', 'en-US')).toBe('Thursday');
    expect(dayName('Thursday', 'en-US', true)).toBe('Thu');
  });

  it('says how often a booking comes back', () => {
    expect(describeRule(rule({ frequency: 'Daily', daysOfWeek: [] }), 'en-US')).toBe('every day');
    expect(describeRule(rule({ frequency: 'Daily', interval: 3, daysOfWeek: [] }), 'en-US')).toBe(
      'every 3 days',
    );
    expect(describeRule(rule(), 'en-US')).toBe('every Monday');
    expect(describeRule(rule({ daysOfWeek: ['Thursday', 'Monday'] }), 'en-US')).toBe(
      'every Monday and Thursday',
    );
    expect(describeRule(rule({ interval: 2, daysOfWeek: ['Monday'] }), 'en-US')).toBe(
      'every 2 weeks on Monday',
    );
  });

  it('calls Monday to Friday every weekday, but only every week', () => {
    const weekdays = rule({ daysOfWeek: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] });

    expect(describeRule(weekdays, 'en-US')).toBe('every weekday');
    expect(describeRule({ ...weekdays, interval: 2 }, 'en-US')).toBe(
      'every 2 weeks on Monday, Tuesday, Wednesday, Thursday, and Friday',
    );
  });

  it('says until when', () => {
    expect(describeSeries(rule(), 'en-US')).toBe('every Monday until 2 Nov 2026');
  });

  it("works its dates out in the browser's time zone", () => {
    expect(browserTimeZone()).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
  });
});
