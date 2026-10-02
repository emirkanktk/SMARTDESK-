import { InboxMessage } from '../app/core/models/inbox.model';

/** An invitation to Board Room on Mon 5 Oct, 10:00-11:00 local time, from Zeynep Kaya, unread. */
export function inboxMessage(overrides: Partial<InboxMessage> = {}): InboxMessage {
  return {
    id: 'msg-1',
    recipientId: 'u-ayse',
    kind: 'MeetingInvitation',
    reservationId: 'rsv-1',
    senderId: 'u-zeynep',
    senderName: 'Zeynep Kaya',
    resourceName: 'Board Room',
    startDateTime: new Date('2026-10-05T10:00').toISOString(),
    endDateTime: new Date('2026-10-05T11:00').toISOString(),
    note: 'Sprint planning',
    reason: null,
    createdAt: new Date('2026-10-01T09:00').toISOString(),
    readAt: null,
    ...overrides,
  };
}
