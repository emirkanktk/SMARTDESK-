import { DOCUMENT, formatDate } from '@angular/common';
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
import { resourceStatusLabel } from '../../core/i18n/labels';
import { DoorBooking, DoorScreen as DoorScreenData } from '../../core/models/door-screen.model';
import { ApiError } from '../../core/models/api-error.model';
import { CHECK_IN } from '../../core/models/reservation.model';
import { DoorScreenService } from '../../core/services/door-screen.service';
import { QrCode } from '../../shared/components/qr-code/qr-code';

/** How often the screen asks again at most; it asks sooner when its code is about to change. */
export const DOOR_REFRESH_MS = 10_000;

const TOKEN_KEY = (resourceId: string) => `smartdesk_door_${resourceId}`;

/** What the room is doing now, as the screen says it in big letters. */
export type DoorState =
  | { kind: 'unavailable'; reason: string; until: Date | null }
  | { kind: 'busy'; booking: DoorBooking; until: Date; waitingUntil: Date | null }
  | { kind: 'free'; until: Date | null };

/**
 * The screen at a resource's door (`/door/:resourceId`, ADR-0034): the room, whether it's free
 * or in use and until when, the rest of today's bookings as times, and the QR code to check in,
 * whose code changes every 30 seconds. It has no user: the link it was opened with carries the
 * screen's token after '#', kept here and then taken out of the address.
 */
@Component({
  imports: [QrCode],
  selector: 'app-door-screen',
  styleUrl: './door-screen.scss',
  templateUrl: './door-screen.html',
})
export class DoorScreen implements OnInit {
  readonly resourceId = input.required<string>();

  private readonly service = inject(DoorScreenService);
  private readonly document = inject(DOCUMENT);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly screen = signal<DoorScreenData | null>(null);
  /** No token, or one that's no longer the resource's screen's. */
  protected readonly notSetUp = signal(false);
  /** The last request failed for another reason; what's shown may be out of date. */
  protected readonly offline = signal(false);
  /** The screen's clock, moved on by the server's on every answer. */
  private readonly now = signal(Date.now());
  private offsetMs = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private token: string | null = null;

  protected readonly state = computed(() => {
    const screen = this.screen();
    return screen ? doorState(screen, this.now()) : null;
  });
  /** Today's bookings still to come, after the one in progress. */
  protected readonly upcoming = computed(() => {
    const now = this.now();
    return (this.screen()?.bookings ?? []).filter((b) => new Date(b.startDateTime).getTime() > now);
  });
  protected readonly qrLabel = computed(
    () =>
      $localize`:@@door.qrLabel:QR code for checking in to ${this.screen()?.resourceName ?? ''}:resource:`,
  );
  protected readonly statusLabel = resourceStatusLabel;

  ngOnInit(): void {
    this.token = this.takeToken();
    if (!this.token) {
      this.notSetUp.set(true);
      return;
    }
    const clock = setInterval(() => this.now.set(Date.now() + this.offsetMs), 1_000);
    this.destroyRef.onDestroy(() => {
      clearInterval(clock);
      if (this.timer !== null) clearTimeout(this.timer);
    });
    this.load();
  }

  protected time(value: string | Date): string {
    return formatDate(value, 'HH:mm', this.locale);
  }

  protected clock(): string {
    return formatDate(this.now(), 'HH:mm', this.locale);
  }

  protected date(): string {
    return formatDate(this.now(), 'EEEE d MMMM', this.locale);
  }

  /** How much of the current code's 30 seconds is left, for the bar under the QR code. */
  protected codeLeft(): number {
    const changesAt = this.screen()?.checkIn?.changesAt;
    if (!changesAt) return 0;
    const left = new Date(changesAt).getTime() - this.now();
    return Math.max(0, Math.min(1, left / 30_000));
  }

  private load(): void {
    this.service.get(this.resourceId(), this.token!, browserTimeZone()).subscribe({
      next: (screen) => {
        this.offsetMs = new Date(screen.now).getTime() - Date.now();
        this.now.set(Date.now() + this.offsetMs);
        this.screen.set(screen);
        this.offline.set(false);
        this.schedule(nextRefreshMs(screen));
      },
      error: (error: unknown) => {
        const status =
          error instanceof ApiError ? error.status : (error as HttpErrorResponse)?.status;
        if (status === 404) {
          // The link was replaced or the screen turned off: this token is done.
          this.forgetToken();
          this.notSetUp.set(true);
          return;
        }
        this.offline.set(true);
        this.schedule(DOOR_REFRESH_MS);
      },
    });
  }

  private schedule(ms: number): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.load(), ms);
  }

  /** From the address (once, then out of it and the history), or as kept from an earlier visit. */
  private takeToken(): string | null {
    const key = TOKEN_KEY(this.resourceId());
    const fromLink = this.document.location.hash.replace(/^#/, '');
    if (/^[0-9a-f]{64}$/.test(fromLink)) {
      try {
        localStorage.setItem(key, fromLink);
      } catch {
        // A screen that can't keep it works until it's reloaded.
      }
      const { pathname, search } = this.document.location;
      this.document.defaultView?.history.replaceState(null, '', pathname + search);
      return fromLink;
    }
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  private forgetToken(): void {
    try {
      localStorage.removeItem(TOKEN_KEY(this.resourceId()));
    } catch {
      // Nothing kept, nothing to forget.
    }
  }
}

/** Asks again just after the code changes, and at least every DOOR_REFRESH_MS. */
export function nextRefreshMs(screen: DoorScreenData): number {
  if (!screen.checkIn) return DOOR_REFRESH_MS;
  const untilChange = new Date(screen.checkIn.changesAt).getTime() - new Date(screen.now).getTime();
  return Math.min(DOOR_REFRESH_MS, Math.max(1_000, untilChange + 500));
}

/**
 * What the room is doing at `now`: out of use (its status or a blocked window), in use by a
 * booking (waiting for its check-in until when, if it needs one), or free until the next one.
 */
export function doorState(screen: DoorScreenData, now: number): DoorState {
  const time = (value: string) => new Date(value).getTime();
  if (screen.status !== 'Active') {
    return { kind: 'unavailable', reason: resourceStatusLabel(screen.status), until: null };
  }
  const block = screen.blocks.find(
    (b) => time(b.startDateTime) <= now && now < time(b.endDateTime),
  );
  if (block) {
    return { kind: 'unavailable', reason: block.reason, until: new Date(block.endDateTime) };
  }
  const current = screen.bookings.find(
    (b) => time(b.startDateTime) <= now && now < time(b.endDateTime),
  );
  if (current) {
    const closes = Math.min(
      time(current.startDateTime) + CHECK_IN.graceMinutes * 60_000,
      time(current.endDateTime),
    );
    const waiting = current.checkInNeeded && !current.checkedIn && now < closes;
    return {
      kind: 'busy',
      booking: current,
      until: new Date(current.endDateTime),
      waitingUntil: waiting ? new Date(closes) : null,
    };
  }
  const next = [...screen.bookings, ...screen.blocks]
    .map((b) => time(b.startDateTime))
    .filter((start) => start > now)
    .sort((a, b) => a - b)[0];
  return { kind: 'free', until: next === undefined ? null : new Date(next) };
}

function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}
