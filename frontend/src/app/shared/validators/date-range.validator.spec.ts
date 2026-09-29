import { FormControl, FormGroup } from '@angular/forms';
import { DateRangeOptions, dateRangeValidator } from './date-range.validator';

describe('dateRangeValidator', () => {
  const now = new Date(2026, 9, 5, 12, 0).getTime();

  function validate(start: string, end: string, options: Partial<DateRangeOptions> = {}) {
    const group = new FormGroup(
      { start: new FormControl(start), end: new FormControl(end) },
      {
        validators: dateRangeValidator({
          startKey: 'start',
          endKey: 'end',
          futureOnly: true,
          now: () => now,
          ...options,
        }),
      },
    );
    return group.errors;
  }

  it('accepts a future range with the end after the start', () => {
    expect(validate('2026-10-05T13:00', '2026-10-05T14:00')).toBeNull();
  });

  it('rejects an end that is not after the start', () => {
    expect(validate('2026-10-05T14:00', '2026-10-05T14:00')).toEqual({ rangeOrder: true });
    expect(validate('2026-10-05T14:00', '2026-10-05T13:00')).toEqual({ rangeOrder: true });
  });

  it('rejects a start in the past', () => {
    expect(validate('2026-10-05T11:00', '2026-10-05T13:00')).toEqual({ startInPast: true });
  });

  it('rejects a start beyond the booking horizon, unless it is the one already saved', () => {
    const options = { maxAdvanceDays: 180 };
    // now is 5 Oct 2026 12:00, so the horizon ends on 3 Apr 2027 at 12:00.
    expect(validate('2027-04-03T12:00', '2027-04-03T13:00', options)).toBeNull();
    expect(validate('2027-04-03T12:01', '2027-04-03T13:00', options)).toEqual({
      startTooFarAhead: { maxAdvanceDays: 180 },
    });
    expect(
      validate('2027-05-01T10:00', '2027-05-01T11:00', {
        ...options,
        originalStart: '2027-05-01T10:00',
      }),
    ).toBeNull();
  });

  it('rejects a range longer than maxDays', () => {
    // January-February: no daylight-saving change in any common zone, so 31 calendar days
    // is exactly 31 x 24 h wherever the tests run.
    expect(validate('2027-01-05T00:00', '2027-02-06T00:00', { maxDays: 31 })).toEqual({
      rangeTooLong: { maxDays: 31 },
    });
    expect(validate('2027-01-05T00:00', '2027-02-05T00:00', { maxDays: 31 })).toBeNull();
  });

  it('allows a start up to graceMinutes in the past', () => {
    const options = { graceMinutes: 5 };
    expect(validate('2026-10-05T11:55', '2026-10-05T13:00', options)).toBeNull();
    expect(validate('2026-10-05T11:54', '2026-10-05T13:00', options)).toEqual({
      startInPast: true,
    });
  });

  it('never flags a start still at its originalStart, only a changed one', () => {
    // Loaded at 11:30; it's now 12:00, so the reservation has started meanwhile.
    const editing = { originalStart: '2026-10-05T11:30' };
    expect(validate('2026-10-05T11:30', '2026-10-05T13:00', editing)).toBeNull();
    expect(validate('2026-10-05T11:15', '2026-10-05T13:00', editing)).toEqual({
      startInPast: true,
    });
  });

  it('with endInFuture, rejects an end that has passed and ignores a past start', () => {
    const inProgress = { futureOnly: false, endInFuture: true };
    expect(validate('2026-10-05T11:00', '2026-10-05T12:30', inProgress)).toBeNull();
    expect(validate('2026-10-05T11:00', '2026-10-05T12:00', inProgress)).toEqual({
      endInPast: true,
    });
  });

  it('leaves empty values to the controls own required validators', () => {
    expect(validate('', '2026-10-05T14:00')).toBeNull();
  });
});
