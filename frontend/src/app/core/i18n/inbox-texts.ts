import { formatDate } from '@angular/common';
import { InboxMessage } from '../models/inbox.model';

// The sentences of the inbox (ADR-0032). The API sends what happened and to which meeting;
// the words are written here, in the reader's language.

/** What happened, e.g. "Zeynep Kaya invited you to a meeting". */
export function inboxHeadline(message: InboxMessage): string {
  const sender = message.senderName ?? $localize`:@@common.someone:Someone`;
  switch (message.kind) {
    case 'MeetingInvitation':
      return $localize`:@@inbox.invited:${sender}:sender: invited you to a meeting`;
    case 'MeetingChanged':
      return $localize`:@@inbox.changed:${sender}:sender: changed a meeting you're invited to`;
    case 'MeetingCancelled':
      // Only a release has nobody behind it.
      return message.senderId === null
        ? $localize`:@@inbox.released:A meeting you were invited to was released because nobody checked in`
        : $localize`:@@inbox.cancelled:${sender}:sender: cancelled a meeting you were invited to`;
    case 'MeetingUninvited':
      return $localize`:@@inbox.uninvited:${sender}:sender: removed you from a meeting`;
    case 'MeetingAccepted':
      return $localize`:@@inbox.accepted:${sender}:sender: is coming to your meeting`;
    case 'MeetingDeclined':
      return $localize`:@@inbox.declined:${sender}:sender: can't come to your meeting`;
    case 'MeetingExtended':
      return $localize`:@@inbox.extended:${sender}:sender: extended a meeting you're invited to`;
  }
}

/** "Mon 5 Oct, 10:00-11:00", or with both dates when the meeting runs past midnight. */
export function meetingWhen(
  meeting: Pick<InboxMessage, 'startDateTime' | 'endDateTime'>,
  locale: string,
): string {
  const start = new Date(meeting.startDateTime);
  const end = new Date(meeting.endDateTime);
  const from = formatDate(start, 'EEE d MMM, HH:mm', locale);
  return start.toDateString() === end.toDateString()
    ? `${from}-${formatDate(end, 'HH:mm', locale)}`
    : $localize`:@@common.rangeTo:${from}:start: to ${formatDate(end, 'EEE d MMM, HH:mm', locale)}:end:`;
}

/** The whole message on one line, for a toast or a desktop notification. */
export function inboxSummary(message: InboxMessage, locale: string): string {
  return $localize`:@@inbox.summary:${inboxHeadline(message)}:what:: ${message.resourceName}:resource:, ${meetingWhen(message, locale)}:when:.`;
}
