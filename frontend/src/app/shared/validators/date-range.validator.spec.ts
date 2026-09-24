import { FormControl, FormGroup } from '@angular/forms';
import { dateRangeValidator } from './date-range.validator';

describe('dateRangeValidator', () => {
  const now = new Date(2026, 9, 5, 12, 0).getTime();

  function validate(start: string, end: string, options: { maxDays?: number } = {}) {
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

  it('rejects a range longer than maxDays', () => {
    // January-February: no daylight-saving change in any common zone, so 31 calendar days
    // is exactly 31 x 24 h wherever the tests run.
    expect(validate('2027-01-05T00:00', '2027-02-06T00:00', { maxDays: 31 })).toEqual({
      rangeTooLong: { maxDays: 31 },
    });
    expect(validate('2027-01-05T00:00', '2027-02-05T00:00', { maxDays: 31 })).toBeNull();
  });

  it('leaves empty values to the controls own required validators', () => {
    expect(validate('', '2026-10-05T14:00')).toBeNull();
  });
});
