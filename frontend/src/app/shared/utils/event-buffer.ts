/**
 * Collects live events that arrive while a list is loading, so they can be applied again to
 * the list once it arrives. Without it, a change committed after the server ran the query
 * is shown briefly and then overwritten by the older result.
 *
 *   load starts   -> start()
 *   event arrives -> apply it to what's on screen, and record() it
 *   load arrives  -> replace the list, then apply everything drain() returns
 *
 * Applying an event twice must be harmless (upsert or remove by id).
 */
export class EventBuffer<T> {
  private pending: T[] | null = null;

  /** Starts collecting, dropping anything collected for an earlier load. */
  start(): void {
    this.pending = [];
  }

  /** Keeps the event if a load is in flight; ignores it otherwise. */
  record(event: T): void {
    this.pending?.push(event);
  }

  /** Stops collecting and returns what arrived since start(), oldest first. */
  drain(): T[] {
    const events = this.pending ?? [];
    this.pending = null;
    return events;
  }
}
