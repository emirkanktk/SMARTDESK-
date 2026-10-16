import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, finalize, forkJoin, merge, of, switchMap, tap } from 'rxjs';
import { inboxHeadline, messageWhen } from '../../core/i18n/inbox-texts';
import { INBOX_PAGE_SIZE, InboxMessage, InboxMessageKind } from '../../core/models/inbox.model';
import { Reservation } from '../../core/models/reservation.model';
import { AuthService } from '../../core/services/auth.service';
import { InboxService } from '../../core/services/inbox.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { Icon, IconName } from '../../shared/components/icon/icon';
import { MeetingAnswer } from '../../shared/components/meeting-answer/meeting-answer';
import { EventBuffer } from '../../shared/utils/event-buffer';

const KIND_ICONS: Record<InboxMessageKind, IconName> = {
  MeetingInvitation: 'calendar-plus',
  MeetingChanged: 'compose',
  MeetingCancelled: 'close',
  MeetingUninvited: 'logout',
  MeetingAccepted: 'calendar-check',
  MeetingDeclined: 'close',
  MeetingExtended: 'clock',
  SeriesInvitation: 'calendar-plus',
  SeriesCancelled: 'close',
  SeriesAccepted: 'calendar-check',
  SeriesDeclined: 'close',
  IssueResolved: 'check',
  CancelledByBlock: 'close',
  WaitlistBooked: 'calendar-check',
  ApprovalRequested: 'shield',
  BookingApproved: 'calendar-check',
  BookingDeclined: 'close',
  ApprovalLapsed: 'clock',
};

/** News of a meeting the reader is still invited to, which they can answer. */
const ABOUT_AN_INVITATION: readonly InboxMessageKind[] = [
  'MeetingInvitation',
  'MeetingChanged',
  'MeetingExtended',
  'SeriesInvitation',
];
/** Answers to the reader's own invitations. */
const ABOUT_AN_ANSWER: readonly InboxMessageKind[] = [
  'MeetingAccepted',
  'MeetingDeclined',
  'SeriesAccepted',
  'SeriesDeclined',
];
/**
 * Messages whose quote is their reason: why someone can't come, why a resource closed, or why
 * an Admin declined a booking (ADR-0049).
 */
const WITH_A_REASON: readonly InboxMessageKind[] = [
  'MeetingDeclined',
  'SeriesDeclined',
  'CancelledByBlock',
  'BookingDeclined',
];
/** News of the reader's own booking: one the waiting list made, or what an Admin decided. */
const ABOUT_A_BOOKING: readonly InboxMessageKind[] = [
  'WaitlistBooked',
  'BookingApproved',
  'BookingDeclined',
  'ApprovalLapsed',
];

interface Item {
  message: InboxMessage;
  headline: string;
  /** "Board Room · Mon 5 Oct, 10:00-11:00": the meeting as it was when the message was sent. */
  when: string;
  /** When it arrived: the time today, the day and time this year, the date before that. */
  received: string;
  icon: IconName;
  unread: boolean;
  /**
   * The organizer's note, or why someone can't come in an answer to the reader's own
   * invitation.
   */
  quote: string | null;
  /**
   * The meeting's page, for news of one the reader is still invited to or organizes; the
   * booking's, for news of their own; what waits for approval, for an Admin asked (ADR-0049).
   */
  link: string[] | null;
  linkLabel: string;
  /**
   * The meeting to answer, on the newest message about one the reader is still invited to;
   * older messages about it don't ask again. For a meeting that repeats, its next date.
   */
  meeting: Reservation | null;
  /** The answer is for that date and every later one: an invitation to a series (ADR-0035). */
  following: boolean;
}

/**
 * The signed-in user's messages (ADR-0032), newest first: invitations to meetings, news of the
 * ones they're invited to - moved, cancelled, or taken off the list - and, for an organizer,
 * who's coming and who can't, and why. An invitation can be answered right there. New messages
 * come in at the top as they arrive. Opening a meeting from a message, or answering it, marks
 * it read.
 */
@Component({
  imports: [RouterLink, Icon, MeetingAnswer],
  selector: 'app-inbox',
  styleUrl: './inbox.scss',
  templateUrl: './inbox.html',
})
export class Inbox implements OnInit {
  private readonly inbox = inject(InboxService);
  private readonly reservationService = inject(ReservationService);
  private readonly hub = inject(ReservationHubService);
  private readonly auth = inject(AuthService);
  private readonly locale = inject(LOCALE_ID);

  private readonly messages = signal<InboxMessage[]>([]);
  /** The meetings the reader is invited to and that aren't over, by id. */
  private readonly invitations = signal<ReadonlyMap<string, Reservation>>(new Map());
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly markingAll = signal(false);
  /** Ids of messages being marked read, to disable their buttons meanwhile. */
  protected readonly marking = signal<ReadonlySet<string>>(new Set());

  private readonly loads = new Subject<void>();
  private readonly liveMessages = new EventBuffer<InboxMessage>();

  protected readonly items = computed(() => {
    const now = new Date();
    const invitations = this.invitations();
    const asked = new Set<string>();
    return this.messages().map((message): Item => {
      const id = message.reservationId;
      const seriesId = message.series?.id ?? null;
      const invitation = ABOUT_AN_INVITATION.includes(message.kind);
      const answer = ABOUT_AN_ANSWER.includes(message.kind);
      // Newest first, so the first message about a meeting is the one that asks. A series is
      // asked about from its next date the reader is still invited to.
      const key = seriesId ?? id;
      const found =
        !invitation || key === null || asked.has(key)
          ? null
          : seriesId
            ? nextDate(invitations.values(), seriesId)
            : (invitations.get(id!) ?? null);
      if (found && key) {
        asked.add(key);
      }
      const target = found?.id ?? id;
      return {
        message,
        headline: inboxHeadline(message),
        when: `${message.resourceName} · ${messageWhen(message, this.locale)}`,
        received: this.receivedAt(new Date(message.createdAt), now),
        icon: KIND_ICONS[message.kind],
        unread: message.readAt === null,
        quote: WITH_A_REASON.includes(message.kind)
          ? message.reason
          : answer || message.kind === 'MeetingUninvited'
            ? null
            : message.note,
        link:
          message.kind === 'ApprovalRequested'
            ? ['/admin', 'approvals']
            : target !== null && (invitation || answer || ABOUT_A_BOOKING.includes(message.kind))
              ? ['/reservations', target, 'edit']
              : null,
        linkLabel:
          message.kind === 'ApprovalRequested'
            ? $localize`:@@inbox.openApprovals:See what waits for approval`
            : ABOUT_A_BOOKING.includes(message.kind)
              ? $localize`:@@inbox.openBooking:See the booking`
              : $localize`:@@inbox.openMeeting:See the meeting`,
        meeting: found,
        following: message.kind === 'SeriesInvitation',
      };
    });
  });
  protected readonly anyUnread = computed(
    () => this.inbox.unread() > 0 || this.messages().some((m) => m.readAt === null),
  );
  /** The list is the newest INBOX_PAGE_SIZE: older messages aren't shown. */
  protected readonly capped = computed(() => this.messages().length >= INBOX_PAGE_SIZE);

  constructor() {
    this.loads
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.liveMessages.start();
        }),
        // switchMap: only the newest load counts. Without the invitations the messages still
        // show, only without a way to answer them.
        switchMap(() =>
          forkJoin({
            messages: this.inbox.list(),
            invited: this.reservationService.getInvited().pipe(catchError(() => of([]))),
          }).pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((lists) => {
        this.loading.set(false);
        const missed = this.liveMessages.drain();
        if (lists) {
          this.messages.set(lists.messages);
          this.invitations.set(new Map(lists.invited.map((r) => [r.id, r])));
          this.loadFailed.set(false);
          // The list may predate messages that arrived while it loaded.
          missed.forEach((message) => this.add(message));
        } else {
          this.loadFailed.set(true);
        }
      });

    this.inbox.received$.pipe(takeUntilDestroyed()).subscribe((message) => {
      this.add(message);
      this.liveMessages.record(message);
    });

    // A meeting moved, cancelled, answered elsewhere, or that they were taken off: what can
    // still be answered follows it.
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed())
      .subscribe((reservation) => this.track(reservation));
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loads.next();
    // The badge may be out of step, e.g. after reading messages in another tab.
    this.inbox.refreshUnread();
  }

  protected markRead(item: Item): void {
    const { message } = item;
    if (message.readAt !== null || this.marking().has(message.id)) {
      return;
    }

    this.marking.update((ids) => new Set(ids).add(message.id));
    this.inbox
      .markRead(message)
      .pipe(
        finalize(() =>
          this.marking.update((ids) => {
            const rest = new Set(ids);
            rest.delete(message.id);
            return rest;
          }),
        ),
      )
      .subscribe({
        // A failure already raised a toast; the message stays unread.
        next: () => this.setRead((m) => m.id === message.id),
        error: () => undefined,
      });
  }

  /** The answer was sent: the meeting is as the API now has it, and the message was read. */
  protected onAnswered(meeting: Reservation, item: Item): void {
    this.track(meeting);
    this.markRead(item);
  }

  protected markAllRead(): void {
    this.markingAll.set(true);
    this.inbox
      .markAllRead()
      .pipe(finalize(() => this.markingAll.set(false)))
      .subscribe({
        next: () => this.setRead(() => true),
        error: () => undefined,
      });
  }

  private track(reservation: Reservation): void {
    const userId = this.auth.currentUser()?.id;
    const invited =
      reservation.status !== 'Cancelled' &&
      new Date(reservation.endDateTime).getTime() > Date.now() &&
      reservation.attendees.some((a) => a.id === userId);
    const listed = this.invitations().has(reservation.id);
    if (invited || listed) {
      this.invitations.update((all) => {
        const next = new Map(all);
        if (invited) {
          next.set(reservation.id, reservation);
        } else {
          next.delete(reservation.id);
        }
        return next;
      });
    }
  }

  private setRead(which: (message: InboxMessage) => boolean): void {
    const now = new Date().toISOString();
    this.messages.update((list) =>
      list.map((m) => (m.readAt === null && which(m) ? { ...m, readAt: now } : m)),
    );
  }

  /**
   * A message that just arrived goes on top, being the newest. One the list already has (it
   * arrived during the load, and the load has it too) stays where it is.
   */
  private add(message: InboxMessage): void {
    this.messages.update((list) =>
      list.some((m) => m.id === message.id) ? list : [message, ...list],
    );
  }

  private receivedAt(date: Date, now: Date): string {
    const format =
      date.toDateString() === now.toDateString()
        ? 'HH:mm'
        : date.getFullYear() === now.getFullYear()
          ? 'EEE d MMM, HH:mm'
          : 'd MMM y';
    return formatDate(date, format, this.locale);
  }
}

/** The soonest date of a series among the meetings the reader is invited to. */
function nextDate(meetings: Iterable<Reservation>, seriesId: string): Reservation | null {
  let next: Reservation | null = null;
  for (const meeting of meetings) {
    if (meeting.series?.id === seriesId && (!next || meeting.startDateTime < next.startDateTime)) {
      next = meeting;
    }
  }
  return next;
}
