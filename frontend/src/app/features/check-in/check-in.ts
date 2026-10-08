import { formatDate } from '@angular/common';
import {
  Component,
  DestroyRef,
  LOCALE_ID,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, merge, of } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { issueCategoryLabel, issueStatusLabel } from '../../core/i18n/labels';
import { IssueReport } from '../../core/models/issue.model';
import { Reservation, checkInWindow } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { IssueService } from '../../core/services/issue.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import { ExtendBooking } from '../../shared/components/extend-booking/extend-booking';
import { ResourcePhoto } from '../../shared/components/resource-photo/resource-photo';
import { addDays, startOfDay } from '../../shared/utils/date-time';
import { ReportIssue } from './report-issue/report-issue';

/** What the page offers for the caller's bookings of the resource, at a given moment. */
export type Visit =
  /** One is open for its check-in and waiting for it. */
  | { kind: 'open'; reservation: Reservation; closesAt: Date }
  /** One is checked in and not over yet. */
  | { kind: 'checkedIn'; reservation: Reservation }
  /** One's check-in closed with nobody checked in; it's released at the next run. */
  | { kind: 'closed'; reservation: Reservation; closedAt: Date }
  /** The next one's check-in hasn't opened yet. */
  | { kind: 'later'; reservation: Reservation; opensAt: Date }
  /** Nothing else to come, but one was released today because nobody checked in. */
  | { kind: 'released'; reservation: Reservation }
  | { kind: 'none' };

/** How often the page looks at the clock again, so a check-in that opens or closes shows. */
export const CHECK_IN_TICK_MS = 15_000;

/** What can be booked at the door with a click, in minutes (ADR-0034). */
export const BOOK_NOW_OPTIONS = [30, 60] as const;

/** The room right now, for someone at its door with nothing of theirs to check in to. */
export type RoomNow =
  { kind: 'unavailable' } | { kind: 'busy'; until: Date } | { kind: 'free'; until: Date | null };

/**
 * The page a resource's QR code opens (`/check-in/:resourceId?code=…`, ADR-0030): the
 * caller's booking of it, or a meeting there they're invited to (ADR-0032), and a button to
 * check in while that's open. The API decides; this page only shows what it will say, and
 * what it said.
 *
 * At the door it does more (ADR-0034): the code is taken out of the address and the history
 * as soon as it's read; a booking checked in says who did it, and its owner can make it run
 * longer or end it there; and someone with nothing to check in to sees whether the room is
 * free, and can book it on the spot, checked in at once.
 *
 * And anyone there can report a problem with it (ADR-0037), seeing first the ones already
 * reported and not fixed yet. That's why the page is the resource's own, whether or not its
 * bookings need a check-in.
 */
@Component({
  imports: [RouterLink, ExtendBooking, ResourcePhoto, ReportIssue],
  selector: 'app-check-in',
  styleUrl: './check-in.scss',
  templateUrl: './check-in.html',
})
export class CheckIn implements OnInit {
  /** Route parameter. */
  readonly resourceId = input.required<string>();
  /** The secret from the QR code, as a query parameter. */
  readonly code = input<string>();

  private readonly resourceService = inject(ResourceService);
  private readonly reservationService = inject(ReservationService);
  private readonly hub = inject(ReservationHubService);
  private readonly auth = inject(AuthService);
  private readonly blocks = inject(ResourceBlockService);
  private readonly issues = inject(IssueService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * The code from the QR code, kept here once read: it's taken out of the address so it
   * doesn't stay in the history, or get passed on with a copied link.
   */
  protected readonly secret = signal<string | null>(null);
  /** The rest of today on this resource: everyone's bookings, and its blocked windows. */
  private readonly day = signal<{ bookings: Reservation[]; blocks: ResourceBlock[] }>({
    bookings: [],
    blocks: [],
  });
  protected readonly booking = signal<number | null>(null);
  protected readonly ending = signal(false);
  protected readonly extendOpen = signal(false);

  protected readonly resource = signal<Resource | null>(null);
  /** The caller's own bookings and the meetings they're invited to. */
  private readonly mine = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly checkingIn = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly now = signal(Date.now());

  /** The problems reported here and not fixed yet; blocking ones first, then the newest. */
  protected readonly openIssues = signal<IssueReport[]>([]);
  protected readonly reporting = signal(false);
  protected readonly categoryLabel = issueCategoryLabel;
  protected readonly statusLabel = issueStatusLabel;

  protected readonly visit = computed(() => visitFor(this.mine(), this.resourceId(), this.now()));
  protected readonly roomNow = computed(() => {
    const resource = this.resource();
    return resource ? roomNowFor(resource, this.day(), this.now()) : null;
  });
  /** The lengths that fit before the room's next booking or block. */
  protected readonly bookNowOptions = computed(() => {
    const room = this.roomNow();
    if (room?.kind !== 'free') return [];
    const now = this.now();
    return BOOK_NOW_OPTIONS.filter((m) => !room.until || now + m * 60_000 <= room.until.getTime());
  });

  ngOnInit(): void {
    const code = this.code();
    if (code) {
      this.secret.set(code);
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { code: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }

    const from = new Date();
    const to = addDays(startOfDay(from), 1);
    const range = { from: from.toISOString(), to: to.toISOString(), resourceId: this.resourceId() };
    forkJoin({
      resource: this.resourceService.getResource(this.resourceId()),
      mine: this.reservationService.getMine(),
      invited: this.reservationService.getInvited(),
      // Only for the room's status and its known problems: the page works without them.
      bookings: this.reservationService.getCalendar(range).pipe(catchError(() => of([]))),
      blocks: this.blocks.getBlocks(range).pipe(catchError(() => of([]))),
      issues: this.issues.getOpen(this.resourceId()).pipe(catchError(() => of([]))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ resource, mine, invited, bookings, blocks, issues }) => {
          this.resource.set(resource);
          this.mine.set([...mine, ...invited]);
          this.day.set({ bookings, blocks });
          this.openIssues.set(issues);
        },
        error: () => this.loadFailed.set(true),
      });

    const timer = setInterval(() => this.now.set(Date.now()), CHECK_IN_TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));

    // A booking moved, cancelled, released or checked in from another tab shows here at once.
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((reservation) => {
        if (this.involvesMe(reservation)) {
          this.upsert(reservation);
        } else {
          // Taken off a meeting's list: it's no longer the caller's to check in.
          this.mine.update((list) => list.filter((r) => r.id !== reservation.id));
        }
        if (reservation.resourceId === this.resourceId()) {
          this.followDay(reservation);
        }
      });
  }

  /** "Checked in by Mehmet Demir at 09:58", when it was someone else. */
  protected checkedInBy(reservation: Reservation): string | null {
    if (!reservation.checkedInAt || !reservation.checkedInBy) return null;
    if (reservation.checkedInBy === this.auth.currentUser()?.id) return null;
    const name = reservation.checkedInByName ?? $localize`:@@common.someone:Someone`;
    return $localize`:@@checkIn.checkedInBy:Checked in by ${name}:name: at ${this.when(reservation.checkedInAt)}:time:.`;
  }

  /** The owner's own booking, under way: it can run longer or end at the door. */
  protected underWay(reservation: Reservation): boolean {
    return (
      !this.invited(reservation) && new Date(reservation.startDateTime).getTime() <= this.now()
    );
  }

  /** Books the free room from now, and checks in at once: the caller is at the door. */
  protected bookNow(minutes: number): void {
    const room = this.resource();
    if (!room || this.booking() !== null) return;

    const start = new Date();
    this.booking.set(minutes);
    this.error.set(null);
    this.reservationService
      .create({
        resourceId: room.id,
        startDateTime: start.toISOString(),
        endDateTime: new Date(start.getTime() + minutes * 60_000).toISOString(),
        note: null,
        reminderMinutes: null,
      })
      .pipe(finalize(() => this.booking.set(null)))
      .subscribe({
        next: (reservation) => {
          this.upsert(reservation);
          this.followDay(reservation);
          this.now.set(Date.now());
          this.notifications.success(
            $localize`:@@checkIn.booked:${room.name}:resource: is yours until ${this.when(reservation.endDateTime)}:time:.`,
          );
          if (room.checkInRequired && this.secret()) {
            this.checkIn();
          }
        },
        error: (error: unknown) => this.error.set(this.reason(error)),
      });
  }

  /** "We're done": the booking ends now and the room is free for others. */
  protected endNow(reservation: Reservation): void {
    const name = reservation.resourceName;
    if (
      !confirm(
        $localize`:@@checkIn.endConfirm:End your booking of ${name}:resource: now? The room is free for others at once.`,
      )
    ) {
      return;
    }

    this.ending.set(true);
    this.error.set(null);
    this.reservationService
      .end(reservation.id)
      .pipe(finalize(() => this.ending.set(false)))
      .subscribe({
        next: (ended) => {
          this.upsert(ended);
          this.followDay(ended);
          this.now.set(Date.now());
          this.notifications.success($localize`:@@checkIn.ended:${name}:resource: is free again.`);
        },
        error: (error: unknown) => this.error.set(this.reason(error)),
      });
  }

  /** The report went in: it's one of the known problems now, and the form closes. */
  protected onReported(issue: IssueReport): void {
    this.reporting.set(false);
    this.openIssues.update((list) => sortIssues([issue, ...list.filter((i) => i.id !== issue.id)]));
    this.notifications.success(
      $localize`:@@checkIn.reported:Thanks. The problem is reported, and whoever looks after ${issue.resourceName}:resource: is told.`,
    );
  }

  /** Someone reported the same problem meanwhile: show it, so it's clear why. */
  protected refreshIssues(): void {
    this.issues
      .getOpen(this.resourceId())
      .pipe(catchError(() => of(null)))
      .subscribe((issues) => {
        if (issues) {
          this.openIssues.set(issues);
        }
      });
  }

  protected onExtended(reservation: Reservation): void {
    this.extendOpen.set(false);
    this.upsert(reservation);
    this.followDay(reservation);
  }

  /** Someone else's meeting the caller is invited to, rather than their own booking. */
  protected invited(reservation: Reservation): boolean {
    return reservation.userId !== this.auth.currentUser()?.id;
  }

  protected checkIn(): void {
    const code = this.secret();
    if (!code || this.checkingIn()) {
      return;
    }

    this.checkingIn.set(true);
    this.error.set(null);
    this.resourceService
      .checkIn(this.resourceId(), code)
      .pipe(finalize(() => this.checkingIn.set(false)))
      .subscribe({
        next: (reservation) => {
          this.now.set(Date.now());
          this.upsert(reservation);
        },
        error: (error: unknown) => this.error.set(this.reason(error)),
      });
  }

  private reason(error: unknown): string {
    return error instanceof ApiError && error.status !== 0 && error.status < 500
      ? (error.detail ?? error.title)
      : $localize`:@@checkIn.failed:The check-in could not be sent. Check your connection and try again.`;
  }

  /** Keeps the room's day current: a booking made, moved, ended or cancelled elsewhere. */
  private followDay(reservation: Reservation): void {
    this.day.update((day) => ({
      ...day,
      bookings: [
        ...day.bookings.filter((r) => r.id !== reservation.id),
        ...(reservation.status === 'Confirmed' ? [reservation] : []),
      ],
    }));
  }

  /** "14:30" today, "Fri 9 Oct, 14:30" on another day. */
  protected when(value: string | Date): string {
    const date = new Date(value);
    const sameDay = new Date(this.now()).toDateString() === date.toDateString();
    return formatDate(date, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
  }

  private involvesMe(reservation: Reservation): boolean {
    const userId = this.auth.currentUser()?.id;
    return reservation.userId === userId || reservation.attendees.some((a) => a.id === userId);
  }

  private upsert(reservation: Reservation): void {
    this.mine.update((list) => [...list.filter((r) => r.id !== reservation.id), reservation]);
  }
}

/**
 * What to offer for the caller's bookings of `resourceId` at `now`, the meetings they're
 * invited to included, first match wins: one to check in to now, one checked in and running,
 * one whose check-in closed and that's about to be released, the next one to come, one
 * released today.
 */
export function visitFor(mine: Reservation[], resourceId: string, now: number): Visit {
  const time = (value: string) => new Date(value).getTime();
  const running = mine
    .filter(
      (r) =>
        r.resourceId === resourceId &&
        r.status === 'Confirmed' &&
        r.checkInRequired &&
        time(r.endDateTime) > now,
    )
    .sort((a, b) => time(a.startDateTime) - time(b.startDateTime));

  for (const reservation of running) {
    const { opensAt, closesAt } = checkInWindow(reservation);
    if (!reservation.checkedInAt && opensAt.getTime() <= now && now < closesAt.getTime()) {
      return { kind: 'open', reservation, closesAt };
    }
  }

  // Checked in, perhaps a few minutes before its start, and not over.
  const checkedIn = running.find((r) => r.checkedInAt);
  if (checkedIn) {
    return { kind: 'checkedIn', reservation: checkedIn };
  }

  // Its check-in closed with nobody checked in. The release job cancels it at its next run,
  // up to CheckIn:IntervalSeconds later; until then it still holds the slot.
  const missed = running.find((r) => !r.checkedInAt && checkInWindow(r).closesAt.getTime() <= now);
  if (missed) {
    return { kind: 'closed', reservation: missed, closedAt: checkInWindow(missed).closesAt };
  }

  const next = running.find((r) => !r.checkedInAt && checkInWindow(r).opensAt.getTime() > now);
  if (next) {
    return { kind: 'later', reservation: next, opensAt: checkInWindow(next).opensAt };
  }

  const today = new Date(now).toDateString();
  const released = mine
    .filter(
      (r) =>
        r.resourceId === resourceId &&
        r.releasedAt !== null &&
        new Date(r.releasedAt).toDateString() === today,
    )
    .sort((a, b) => time(b.releasedAt!) - time(a.releasedAt!))[0];
  return released ? { kind: 'released', reservation: released } : { kind: 'none' };
}

/**
 * The room at `now`, for someone with nothing of theirs to check in to: out of use (its status
 * or a blocked window), in use until a booking ends, or free until the next booking or block.
 */
export function roomNowFor(
  resource: Resource,
  day: { bookings: Reservation[]; blocks: ResourceBlock[] },
  now: number,
): RoomNow {
  const time = (value: string) => new Date(value).getTime();
  const covers = (item: { startDateTime: string; endDateTime: string }) =>
    time(item.startDateTime) <= now && now < time(item.endDateTime);
  if (resource.status !== 'Active' || day.blocks.some(covers)) {
    return { kind: 'unavailable' };
  }
  const current = day.bookings.find((r) => r.status === 'Confirmed' && covers(r));
  if (current) {
    return { kind: 'busy', until: new Date(current.endDateTime) };
  }
  const next = [...day.bookings.filter((r) => r.status === 'Confirmed'), ...day.blocks]
    .map((item) => time(item.startDateTime))
    .filter((start) => start > now)
    .sort((a, b) => a - b)[0];
  return { kind: 'free', until: next === undefined ? null : new Date(next) };
}

/** As the API lists them: those that make the resource unusable first, then the newest. */
export function sortIssues(issues: IssueReport[]): IssueReport[] {
  return [...issues].sort(
    (a, b) =>
      Number(b.isBlocking) - Number(a.isBlocking) ||
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );
}
