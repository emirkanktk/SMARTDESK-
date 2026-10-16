import { resourceStatusLabel } from '../../core/i18n/labels';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource } from '../../core/models/resource.model';

/**
 * What a room is doing at a moment: out of use, saying why (its status, or the block's reason)
 * and, for a block, until when; in use until a booking ends; or free until the next booking or
 * block. The check-in page shows it at the door (ADR-0034), the floor plan for every room on a
 * floor (ADR-0041).
 */
export type RoomNow =
  | { kind: 'unavailable'; reason: string; until: Date | null }
  | { kind: 'busy'; until: Date }
  | { kind: 'free'; until: Date | null };

/** The room at `now`, from that day's bookings and blocks of it. */
export function roomNowFor(
  resource: Resource,
  day: { bookings: Reservation[]; blocks: ResourceBlock[] },
  now: number,
): RoomNow {
  const time = (value: string) => new Date(value).getTime();
  const covers = (item: { startDateTime: string; endDateTime: string }) =>
    time(item.startDateTime) <= now && now < time(item.endDateTime);
  if (resource.status !== 'Active') {
    return { kind: 'unavailable', reason: resourceStatusLabel(resource.status), until: null };
  }
  const block = day.blocks.find(covers);
  if (block) {
    return { kind: 'unavailable', reason: block.reason, until: new Date(block.endDateTime) };
  }
  // Waiting for approval holds the time too (ADR-0049).
  const current = day.bookings.find((r) => r.status !== 'Cancelled' && covers(r));
  if (current) {
    return { kind: 'busy', until: new Date(current.endDateTime) };
  }
  const next = [...day.bookings.filter((r) => r.status !== 'Cancelled'), ...day.blocks]
    .map((item) => time(item.startDateTime))
    .filter((start) => start > now)
    .sort((a, b) => a - b)[0];
  return { kind: 'free', until: next === undefined ? null : new Date(next) };
}
