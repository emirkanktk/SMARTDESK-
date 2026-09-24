import { EventBuffer } from './event-buffer';

describe('EventBuffer', () => {
  it('returns the events recorded since start, oldest first', () => {
    const buffer = new EventBuffer<string>();
    buffer.start();
    buffer.record('a');
    buffer.record('b');

    expect(buffer.drain()).toEqual(['a', 'b']);
  });

  it('ignores events while no load is in flight', () => {
    const buffer = new EventBuffer<string>();
    buffer.record('before');
    buffer.start();
    buffer.record('during');
    buffer.drain();
    buffer.record('after');

    expect(buffer.drain()).toEqual([]);
  });

  it('starts over when a newer load replaces the one in flight', () => {
    const buffer = new EventBuffer<string>();
    buffer.start();
    buffer.record('for the cancelled load');
    buffer.start();
    buffer.record('for the new load');

    expect(buffer.drain()).toEqual(['for the new load']);
  });
});
