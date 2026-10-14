import { fakeKindRules, fakeOfficeHours } from '../../../testing/booking-rules-fixtures';
import {
  describeRules,
  minutesOf,
  ruleProblems,
  withinOfficeHours,
  zonedTime,
} from './booking-rules';

// A kind's own rules on the client (ADR-0046), as the API applies them.
describe('booking rules', () => {
  // Istanbul is UTC+3 all year, so these read the same in any browser zone.
  const istanbul = fakeOfficeHours({ timeZone: 'Europe/Istanbul' });
  /** A time in Istanbul on a day of October 2026 (the 5th is a Monday), as an instant. */
  const at = (day: number, time: string) =>
    new Date(`2026-10-${String(day).padStart(2, '0')}T${time}:00+03:00`);
  const now = at(5, '08:00');

  it("reads the office's day and time, wherever the browser is", () => {
    const late = zonedTime(new Date('2026-10-05T22:30:00Z'), 'Europe/Istanbul');

    expect(late.weekDay).toBe('Tuesday');
    expect(late.minutes).toBe(90);
    expect(late.day - zonedTime(now, 'Europe/Istanbul').day).toBe(1);
    expect(minutesOf('08:30:00')).toBe(510);
  });

  it('is within office hours from opening to closing, on one workday', () => {
    expect(withinOfficeHours(istanbul, at(5, '09:00'), at(5, '18:00'))).toBe(true);
    expect(withinOfficeHours(istanbul, at(5, '08:45'), at(5, '10:00'))).toBe(false);
    expect(withinOfficeHours(istanbul, at(5, '17:00'), at(5, '18:15'))).toBe(false);
    expect(withinOfficeHours(istanbul, at(10, '10:00'), at(10, '11:00'))).toBe(false); // Saturday
  });

  it('says nothing for a kind with no rules of its own', () => {
    expect(ruleProblems(fakeKindRules(), istanbul, at(20, '20:00'), at(22, '07:00'), now)).toEqual({
      start: null,
      end: null,
    });
  });

  it('says which rule a time breaks, on its field', () => {
    const desks = fakeKindRules({
      maxDurationMinutes: 240,
      maxDaysAhead: 7,
      officeHoursOnly: true,
    });

    expect(ruleProblems(desks, istanbul, at(6, '09:00'), at(6, '14:00'), now)).toEqual({
      start: null,
      end: 'Desk bookings can last at most 4 h.',
    });
    expect(ruleProblems(desks, istanbul, at(13, '09:00'), at(13, '10:00'), now).start).toBe(
      'Desk bookings can start at most 7 days ahead.',
    );
    expect(ruleProblems(desks, istanbul, at(12, '17:00'), at(12, '19:00'), now).start).toBe(
      'Desk bookings must be within office hours: Mon-Fri 09:00-18:00.',
    );
    expect(ruleProblems(desks, istanbul, at(12, '09:00'), at(12, '13:00'), now)).toEqual({
      start: null,
      end: null,
    });
  });

  it("counts days ahead on the office's calendar, 0 being today only", () => {
    const today = fakeKindRules({ maxDaysAhead: 0 });

    expect(ruleProblems(today, istanbul, at(5, '23:00'), at(5, '23:30'), now).start).toBeNull();
    expect(ruleProblems(today, istanbul, at(6, '00:00'), at(6, '01:00'), now).start).toBe(
      'Desk bookings can only be made for today.',
    );
  });

  it('puts the rules in a few words each, only those set', () => {
    expect(describeRules(fakeKindRules(), istanbul)).toEqual([]);
    expect(
      describeRules(
        fakeKindRules({
          maxDurationMinutes: 90,
          maxDaysAhead: 1,
          maxActivePerUser: 2,
          officeHoursOnly: true,
        }),
        istanbul,
      ),
    ).toEqual([
      'within office hours (Mon-Fri 09:00-18:00)',
      'at most 1 h 30 min long',
      'up to 1 day ahead',
      'at most 2 at a time per person',
    ]);
  });
});
