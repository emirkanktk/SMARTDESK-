import { Reservation } from '../app/core/models/reservation.model';
import { MEETING_ROOM_TYPE } from './resource-fixtures';

export const OWNER_ID = 'user-owner';

export function fakeReservation(overrides: Partial<Reservation> = {}): Reservation {
  return {
    id: 'rsv-1',
    resourceId: 'res-1',
    resourceName: 'Meeting Room A',
    resourceTypeId: MEETING_ROOM_TYPE.id,
    resourceTypeName: MEETING_ROOM_TYPE.name,
    resourceLocation: '2nd Floor',
    userId: OWNER_ID,
    startDateTime: '2026-10-07T11:00:00.000Z',
    endDateTime: '2026-10-07T12:30:00.000Z',
    status: 'Confirmed',
    note: 'Sprint planning',
    reminderMinutes: null,
    checkInRequired: false,
    checkedInAt: null,
    releasedAt: null,
    userDisplayName: 'Ayşe Yılmaz',
    attendees: [],
    ...overrides,
  };
}
