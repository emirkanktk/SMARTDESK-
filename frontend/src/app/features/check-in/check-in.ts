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
import { RouterLink } from '@angular/router';
import { finalize, forkJoin, merge } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Reservation, checkInWindow } from '../../core/models/reservation.model';
import { Resource } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { ResourcePhoto } from '../../shared/components/resource-photo/resource-photo';

/** What the page offers for the caller's bookings of the resource, at a given moment. */
export type Visit =
  /** One is open for its check-in and waiting for it. */
  | { kind: 'open'; reservation: Reservation; closesAt: Date }
  /** One is checked in and not over yet. */
  | { kind: 'checkedIn'; reservation: Reservation }
  /** The next one's check-in hasn't opened yet. */
  | { kind: 'later'; reservation: Reservation; opensAt: Date }
  /** Nothing else to come, but one was released today because nobody checked in. */
  | { kind: 'released'; reservation: Reservation }
  | { kind: 'none' };

/** How often the page looks at the clock again, so a check-in that opens or closes shows. */
export const CHECK_IN_TICK_MS = 15_000;

/**
 * The page a resource's QR code opens (`/check-in/:resourceId?code=…`, ADR-0030): the
 * caller's booking of it, and a button to check in while that's open. The API decides; this
 * page only shows what it will say, and what it said.
 */
@Component({
  imports: [RouterLink, ResourcePhoto],
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
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly resource = signal<Resource | null>(null);
  private readonly mine = signal<Reservation[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly checkingIn = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly now = signal(Date.now());

  protected readonly visit = computed(() => visitFor(this.mine(), this.resourceId(), this.now()));

  ngOnInit(): void {
    forkJoin({
      resource: this.resourceService.getResource(this.resourceId()),
      mine: this.reservationService.getMine(),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ resource, mine }) => {
          this.resource.set(resource);
          this.mine.set(mine);
        },
        error: () => this.loadFailed.set(true),
      });

    const timer = setInterval(() => this.now.set(Date.now()), CHECK_IN_TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));

    // A booking moved, cancelled, released or checked in from another tab shows here at once.
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((reservation) => {
        if (reservation.userId === this.auth.currentUser()?.id) {
          this.upsert(reservation);
        }
      });
  }

  protected checkIn(): void {
    const code = this.code();
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
        error: (error: unknown) =>
          this.error.set(
            error instanceof ApiError && error.status !== 0 && error.status < 500
              ? (error.detail ?? error.title)
              : $localize`:@@checkIn.failed:The check-in could not be sent. Check your connection and try again.`,
          ),
      });
  }

  /** "14:30" today, "Fri 9 Oct, 14:30" on another day. */
  protected when(value: string | Date): string {
    const date = new Date(value);
    const sameDay = new Date(this.now()).toDateString() === date.toDateString();
    return formatDate(date, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
  }

  private upsert(reservation: Reservation): void {
    this.mine.update((list) => [...list.filter((r) => r.id !== reservation.id), reservation]);
  }
}

/**
 * What to offer for the caller's bookings of `resourceId` at `now`, first match wins: one to
 * check in to now, one checked in and running, the next one to come, one released today.
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
