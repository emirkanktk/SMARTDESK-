import { ApiError } from './api-error.model';
import { bookingSuggestions } from './booking-suggestions.model';

describe('bookingSuggestions', () => {
  const time = { start: '2026-10-05T09:00:00Z', end: '2026-10-05T10:00:00Z' };
  const room = { id: 'res-1', name: 'Meeting Room A', capacity: 8, location: '2nd Floor' };
  const conflict = (suggestions: unknown, status = 409) =>
    new ApiError(status, 'Conflict', null, {}, 'reservation-overlaps', suggestions);

  it('reads the times and resources a 409 suggests', () => {
    expect(bookingSuggestions(conflict({ times: [time], resources: [room] }))).toEqual({
      times: [time],
      resources: [room],
    });
  });

  it('leaves out whatever is not of that shape', () => {
    const odd = { times: [time, { start: 9 }], resources: 'none' };

    expect(bookingSuggestions(conflict(odd))).toEqual({ times: [time], resources: [] });
  });

  it('is null with nothing to offer, or for anything but a 409', () => {
    expect(bookingSuggestions(conflict({ times: [], resources: [] }))).toBeNull();
    expect(bookingSuggestions(conflict(null))).toBeNull();
    expect(bookingSuggestions(conflict({ times: [time] }, 400))).toBeNull();
    expect(bookingSuggestions(new Error('offline'))).toBeNull();
  });
});
