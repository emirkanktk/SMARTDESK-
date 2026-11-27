import { DOCUMENT, formatDate } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { ApiError } from '../../core/models/api-error.model';
import { Lobby as LobbyData, LobbyRoom } from '../../core/models/lobby.model';
import { browserTimeZone } from '../../core/services/dashboard.service';
import { LobbyService } from '../../core/services/lobby.service';
import { QrCode } from '../../shared/components/qr-code/qr-code';
import { DoorState, doorState } from '../door-screen/door-screen';
import { LobbyNews } from './lobby-news';

/** How often the screen asks again. */
export const LOBBY_REFRESH_MS = 30_000;

/** How many of today's meetings still to come it lists. */
export const LOBBY_AGENDA = 6;

const TOKEN_KEY = 'smartdesk_lobby';

/** A room as the screen shows it: what it's doing now, as its door screen would say. */
interface RoomRow {
  room: LobbyRoom;
  state: DoorState;
}

/** A meeting still to come today, in whichever room. */
interface AgendaRow {
  start: string;
  end: string;
  room: string;
  location: string;
}

/**
 * A lobby screen (`/lobby`, ADR-0064): a TV in the office showing, from across the room, which
 * meeting rooms are free now and until when, the meetings still to come today, how many desks are
 * free on each floor, and how many people are in, with a QR code that opens SmartDesk on a phone.
 * The announcements showing (ADR-0065) run along the top, one at a time.
 * Nobody is signed in on it, and it shows no names: the link it was opened with carries the
 * screen's own token after '#', kept here and then taken out of the address. It asks again every
 * 30 seconds, and keeps the TV from going dark while it's shown.
 */
@Component({
  imports: [LobbyNews, QrCode],
  selector: 'app-lobby',
  styleUrl: './lobby.scss',
  templateUrl: './lobby.html',
})
export class Lobby implements OnInit {
  private readonly service = inject(LobbyService);
  private readonly document = inject(DOCUMENT);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly lobby = signal<LobbyData | null>(null);
  /** No token, or one that's no longer a screen's. */
  protected readonly notSetUp = signal(false);
  /** The last request failed for another reason; what's shown may be out of date. */
  protected readonly offline = signal(false);
  /** The screen's clock, set by the server's on every answer. */
  private readonly now = signal(Date.now());
  private offsetMs = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private token: string | null = null;
  private wakeLock: { release(): Promise<void> } | null = null;

  protected readonly rooms = computed<RoomRow[]>(() => {
    const now = this.now();
    return (this.lobby()?.rooms ?? []).map((room) => ({ room, state: doorState(room, now) }));
  });

  protected readonly freeRooms = computed(
    () => this.rooms().filter((row) => row.state.kind === 'free').length,
  );

  /** Today's meetings that haven't started, in any room, earliest first. */
  protected readonly agenda = computed<AgendaRow[]>(() => {
    const now = this.now();
    return (this.lobby()?.rooms ?? [])
      .flatMap((room) =>
        room.bookings
          .filter((booking) => new Date(booking.startDateTime).getTime() > now)
          .map((booking) => ({
            start: booking.startDateTime,
            end: booking.endDateTime,
            room: room.name,
            location: room.location,
          })),
      )
      .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
      .slice(0, LOBBY_AGENDA);
  });

  protected readonly peopleLabel = computed(() =>
    this.lobby()?.peopleToday === 1
      ? $localize`:@@lobby.personIn:person in the office today`
      : $localize`:@@lobby.peopleIn:people in the office today`,
  );

  protected readonly qrLabel = $localize`:@@lobby.qrLabel:QR code that opens SmartDesk`;

  ngOnInit(): void {
    this.token = this.takeToken();
    if (!this.token) {
      this.notSetUp.set(true);
      return;
    }
    const clock = setInterval(() => this.now.set(Date.now() + this.offsetMs), 1_000);
    const onVisible = () => {
      if (this.document.visibilityState === 'visible') void this.keepAwake();
    };
    this.document.addEventListener('visibilitychange', onVisible);
    this.destroyRef.onDestroy(() => {
      clearInterval(clock);
      if (this.timer !== null) clearTimeout(this.timer);
      this.document.removeEventListener('visibilitychange', onVisible);
      void this.wakeLock?.release().catch(() => undefined);
    });
    void this.keepAwake();
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

  private load(): void {
    this.service.get(this.token!, browserTimeZone()).subscribe({
      next: (lobby) => {
        this.offsetMs = new Date(lobby.now).getTime() - Date.now();
        this.now.set(Date.now() + this.offsetMs);
        this.lobby.set(lobby);
        this.offline.set(false);
        this.schedule();
      },
      error: (error: unknown) => {
        const status =
          error instanceof ApiError ? error.status : (error as HttpErrorResponse)?.status;
        if (status === 404) {
          // Taken down: this token is done.
          this.forgetToken();
          this.lobby.set(null);
          this.notSetUp.set(true);
          return;
        }
        this.offline.set(true);
        this.schedule();
      },
    });
  }

  private schedule(): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.load(), LOBBY_REFRESH_MS);
  }

  /**
   * Keeps the screen on while the page is shown, where the browser can (Screen Wake Lock). It lets
   * go by itself when the page is hidden, so it's asked again when it's back.
   */
  private async keepAwake(): Promise<void> {
    const wakeLock = (
      this.document.defaultView?.navigator as
        | { wakeLock?: { request(type: 'screen'): Promise<{ release(): Promise<void> }> } }
        | undefined
    )?.wakeLock;
    if (!wakeLock) return;
    try {
      this.wakeLock = await wakeLock.request('screen');
    } catch {
      // Refused, e.g. on battery saver: the screen may go dark, and that's all.
    }
  }

  /** From the address (once, then out of it and the history), or as kept from an earlier visit. */
  private takeToken(): string | null {
    const fromLink = this.document.location.hash.replace(/^#/, '');
    if (/^[0-9a-f]{64}$/.test(fromLink)) {
      try {
        localStorage.setItem(TOKEN_KEY, fromLink);
      } catch {
        // A screen that can't keep it works until it's reloaded.
      }
      const { pathname, search } = this.document.location;
      this.document.defaultView?.history.replaceState(null, '', pathname + search);
      return fromLink;
    }
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  }

  private forgetToken(): void {
    try {
      localStorage.removeItem(TOKEN_KEY);
    } catch {
      // Nothing kept, nothing to forget.
    }
  }
}
