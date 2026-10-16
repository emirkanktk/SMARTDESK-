// Shapes of the inbox endpoints - see docs/API_CONTRACTS.md, Inbox (ADR-0032).

import { IssueCategory } from './issue.model';
import { ReservationSeries } from './reservation.model';

export const INBOX_MESSAGE_KINDS = [
  'MeetingInvitation',
  'MeetingChanged',
  'MeetingCancelled',
  'MeetingUninvited',
  'MeetingAccepted',
  'MeetingDeclined',
  'MeetingExtended',
  'SeriesInvitation',
  'SeriesCancelled',
  'SeriesAccepted',
  'SeriesDeclined',
  'IssueResolved',
  'CancelledByBlock',
  'WaitlistBooked',
  // Approval (ADR-0049): to every Admin, someone asked; to whoever asked, approved, declined
  // (with the reason) or lapsed at its start.
  'ApprovalRequested',
  'BookingApproved',
  'BookingDeclined',
  'ApprovalLapsed',
] as const;
export type InboxMessageKind = (typeof INBOX_MESSAGE_KINDS)[number];

/**
 * A message about a meeting the reader is, or was, invited to - or, for its organizer, an
 * answer to their invitation. It keeps the meeting as it was when the message was sent, and
 * the app writes the sentence in the reader's language. Also the payload of the hub's
 * InboxMessageReceived event, which only its recipient gets. Since ADR-0037 it also tells
 * someone that a problem they reported is resolved (IssueResolved): its times are then when
 * it was reported and resolved, and its note what they wrote.
 */
export interface InboxMessage {
  id: string;
  recipientId: string;
  kind: InboxMessageKind;
  /** The booking it's about; null once that's gone. */
  reservationId: string | null;
  /** Who did it; null when nobody did, i.e. a meeting released because nobody checked in. */
  senderId: string | null;
  /** "Zeynep Kaya"; null when nobody sent it or the account is gone. */
  senderName: string | null;
  resourceName: string;
  /** UTC ISO-8601, e.g. "2026-10-05T10:00:00Z". */
  startDateTime: string;
  endDateTime: string;
  note: string | null;
  /**
   * Why someone can't come, in a MeetingDeclined or SeriesDeclined message to the organizer;
   * why the resource was closed, in a CancelledByBlock (ADR-0038).
   */
  reason: string | null;
  /**
   * The repeating meeting a Series* message is about (ADR-0035); the times above are then its
   * first date the message covers.
   */
  series?: ReservationSeries | null;
  /** What the problem an IssueResolved message is about was. */
  issueCategory?: IssueCategory | null;
  createdAt: string;
  /** When the reader marked it read; null while it's unread. */
  readAt: string | null;
}

/** Body of `GET /api/inbox/unread-count`. */
export interface UnreadInboxCount {
  unread: number;
}

/** How many messages `GET /api/inbox` returns at most: the newest, read or not. */
export const INBOX_PAGE_SIZE = 100;
