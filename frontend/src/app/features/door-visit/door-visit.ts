import { formatDate } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { issueCategoryLabel, issueStatusLabel } from '../../core/i18n/labels';
import { ApiError } from '../../core/models/api-error.model';
import { DoorBooking } from '../../core/models/door-screen.model';
import { DoorVisit as DoorVisitData } from '../../core/models/door-visit.model';
import { IssueReport, compareIssues } from '../../core/models/issue.model';
import { checkInWindow } from '../../core/models/reservation.model';
import { DoorVisitService } from '../../core/services/door-visit.service';
import { NotificationService } from '../../core/services/notification.service';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
import { Logo } from '../../shared/components/logo/logo';
import { ThemeSwitch } from '../../shared/components/theme-switch/theme-switch';
import { ReportIssue } from '../check-in/report-issue/report-issue';
import { browserTimeZone, doorState } from '../door-screen/door-screen';

/** How often the page asks again while it's open, so a booking made or checked in elsewhere shows. */
export const DOOR_VISIT_REFRESH_MS = 30_000;

/** How often it looks at the clock again, so a check-in that opens or closes shows. */
const TICK_MS = 15_000;

/** What the page offers for checking in at a given moment: whoever's booking it is. */
export type DoorCheckIn =
  /** One is waiting for its check-in, until `closesAt`. */
  | { kind: 'open'; booking: DoorBooking; closesAt: Date }
  /** The one under way, or about to be, is checked in. */
  | { kind: 'checkedIn'; booking: DoorBooking }
  /** The next one that needs it can be checked in from `opensAt`. */
  | { kind: 'later'; booking: DoorBooking; opensAt: Date }
  | { kind: 'none' };

/**
 * The page a resource's QR code opens for someone without an account (`/check-in/:resourceId`
 * while nobody is signed in, ADR-0050). The code in the address is all it has: taken out of the
 * address and the history as soon as it's read, it opens the room's page - what it is, what it's
 * doing now, as its door screen says it - checks in the booking waiting for it, whoever's it is,
 * and reports a problem. Booking on the spot, or anything about one's own bookings, takes an
 * account: signing in comes back to the same page, signed in.
 */
@Component({
  imports: [RouterLink, Logo, LanguageSwitch, ThemeSwitch, ReportIssue],
  selector: 'app-door-visit',
  styleUrl: './door-visit.scss',
  templateUrl: './door-visit.html',
})
export class DoorVisit implements OnInit {
  /** Route parameter. */
  readonly resourceId = input.required<string>();
  /** The secret from the QR code, as a query parameter. */
  readonly code = input<string>();

  private readonly service = inject(DoorVisitService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  /** The code from the QR code, kept here once read: it's taken out of the address. */
  protected readonly secret = signal<string | null>(null);
  protected readonly visit = signal<DoorVisitData | null>(null);
  protected readonly loading = signal(true);
  /** The code isn't the resource's, or no longer is: the one at the door is the way in. */
  protected readonly notFound = signal(false);
  protected readonly loadFailed = signal(false);
  /** A later look failed: what's shown may be out of date. */
  protected readonly stale = signal(false);
  protected readonly checkingIn = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly reporting = signal(false);
  protected readonly categoryLabel = issueCategoryLabel;
  protected readonly statusLabel = issueStatusLabel;

  /** The phone's clock, moved on by the server's on every answer. */
  private readonly now = signal(Date.now());
  private offsetMs = 0;
  private looking = true;

  protected readonly state = computed(() => {
    const visit = this.visit();
    return visit ? doorState(visit, this.now()) : null;
  });
  protected readonly checkIn = computed(() => checkInFor(this.visit()?.bookings ?? [], this.now()));
  /** Signing in comes back here, with the code, to the signed-in page. */
  protected readonly signInQuery = computed(() => {
    const code = this.secret();
    const page = `/check-in/${encodeURIComponent(this.resourceId())}`;
    return { returnUrl: code ? `${page}?code=${encodeURIComponent(code)}` : page };
  });

  ngOnInit(): void {
    const code = this.code();
    if (!code) {
      this.loading.set(false);
      return;
    }

    this.secret.set(code);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { code: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.load();

    const tick = setInterval(() => this.now.set(Date.now() + this.offsetMs), TICK_MS);
    const refresh = setInterval(() => this.looking && this.load(), DOOR_VISIT_REFRESH_MS);
    this.destroyRef.onDestroy(() => {
      clearInterval(tick);
      clearInterval(refresh);
    });
  }

  protected checkInNow(): void {
    const code = this.secret();
    const room = this.visit();
    if (!code || !room || this.checkingIn()) {
      return;
    }

    this.checkingIn.set(true);
    this.error.set(null);
    this.service
      .checkIn(this.resourceId(), code)
      .pipe(finalize(() => this.checkingIn.set(false)))
      .subscribe({
        next: (booking) => {
          this.visit.update(
            (visit) =>
              visit && {
                ...visit,
                bookings: visit.bookings.map((b) =>
                  b.startDateTime === booking.startDateTime ? { ...b, checkedIn: true } : b,
                ),
              },
          );
          this.notifications.success(
            $localize`:@@doorVisit.checkedIn:Checked in. ${room.resourceName}:resource: is kept for the booking until ${this.when(booking.endDateTime)}:time:.`,
          );
        },
        error: (error: unknown) => {
          this.error.set(reason(error));
          // Checked in, moved or released meanwhile: show the room as it is now.
          this.load();
        },
      });
  }

  /** The report went in: it's one of the known problems now, and the form closes. */
  protected onReported(issue: IssueReport): void {
    this.reporting.set(false);
    this.visit.update(
      (visit) =>
        visit && {
          ...visit,
          openIssues: [issue, ...visit.openIssues.filter((i) => i.id !== issue.id)].sort(
            compareIssues,
          ),
        },
    );
    this.notifications.success(
      $localize`:@@checkIn.reported:Thanks. The problem is reported, and whoever looks after ${issue.resourceName}:resource: is told.`,
    );
  }

  /** Someone reported the same problem meanwhile: show it, so it's clear why. */
  protected refresh(): void {
    this.load();
  }

  /** "14:30" today, "Fri 9 Oct, 14:30" on another day. */
  protected when(value: string | Date): string {
    const date = new Date(value);
    const sameDay = new Date(this.now()).toDateString() === date.toDateString();
    return formatDate(date, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
  }

  private load(): void {
    const code = this.secret();
    if (!code) return;

    this.service
      .get(this.resourceId(), code, browserTimeZone())
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (visit) => {
          this.offsetMs = new Date(visit.now).getTime() - Date.now();
          this.now.set(Date.now() + this.offsetMs);
          this.visit.set(visit);
          this.stale.set(false);
        },
        error: (error: unknown) => {
          const status =
            error instanceof ApiError ? error.status : (error as HttpErrorResponse)?.status;
          // A code the resource doesn't take (any more) won't again: stop asking.
          if (status === 404) {
            this.looking = false;
          }
          if (this.visit()) {
            this.stale.set(true);
          } else if (status === 404) {
            this.notFound.set(true);
          } else {
            this.loadFailed.set(true);
          }
        },
      });
  }
}

/**
 * What to offer at `now` from the rest of the day's bookings, earliest first: one waiting for
 * its check-in, one checked in that isn't over, or the next one that needs it.
 */
export function checkInFor(bookings: readonly DoorBooking[], now: number): DoorCheckIn {
  const needed = bookings.filter((b) => b.checkInNeeded);
  for (const booking of needed) {
    const { opensAt, closesAt } = checkInWindow(booking);
    if (!booking.checkedIn && opensAt.getTime() <= now && now < closesAt.getTime()) {
      return { kind: 'open', booking, closesAt };
    }
  }

  const checkedIn = needed.find((b) => b.checkedIn);
  if (checkedIn) {
    return { kind: 'checkedIn', booking: checkedIn };
  }

  const next = needed.find((b) => checkInWindow(b).opensAt.getTime() > now);
  return next
    ? { kind: 'later', booking: next, opensAt: checkInWindow(next).opensAt }
    : { kind: 'none' };
}

/** What the API said, or a plain "try again" when it said nothing useful. */
function reason(error: unknown): string {
  return error instanceof ApiError && error.status !== 0 && error.status < 500
    ? (error.detail ?? error.title)
    : $localize`:@@checkIn.failed:The check-in could not be sent. Check your connection and try again.`;
}
