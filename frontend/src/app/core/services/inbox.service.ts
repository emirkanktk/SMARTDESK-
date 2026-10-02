import { HttpClient } from '@angular/common/http';
import { Injectable, Injector, LOCALE_ID, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { EMPTY, Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { inboxSummary } from '../i18n/inbox-texts';
import { InboxMessage, UnreadInboxCount } from '../models/inbox.model';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';
import { ReservationHubService } from './reservation-hub.service';

/** A new message's toast stays up as long as a reminder's: it's easy to miss otherwise. */
export const INBOX_TOAST_MS = 15_000;

/**
 * The signed-in user's inbox (ADR-0032): the messages, how many are unread for the badge on
 * the Inbox link, and a toast for each new one as it arrives over the hub, plus a desktop
 * notification when the browser allows one, so it's seen from another tab too.
 */
@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);
  private readonly hub = inject(ReservationHubService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly locale = inject(LOCALE_ID);
  private readonly injector = inject(Injector);
  private readonly baseUrl = `${environment.apiUrl}/inbox`;

  private readonly unreadSignal = signal(0);
  /** How many messages are unread; 0 while signed out. */
  readonly unread = this.unreadSignal.asReadonly();

  private readonly receivedSubject = new Subject<InboxMessage>();
  /** Each message as it arrives, for an open inbox to put on top. */
  readonly received$: Observable<InboxMessage> = this.receivedSubject.asObservable();

  /** Each value asks the API for the unread count again, or sets it to 0. */
  private readonly counts = new Subject<'load' | 'clear'>();
  private receivedWhileCounting = false;
  private started = false;

  /** Called once by the app; later calls do nothing. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    // switchMap: only the newest answer counts, e.g. after signing out during a load.
    this.counts
      .pipe(
        tap(() => (this.receivedWhileCounting = false)),
        switchMap((action) =>
          action === 'clear'
            ? of(0)
            : this.http.get<UnreadInboxCount>(`${this.baseUrl}/unread-count`).pipe(
                map((count) => count.unread),
                catchError(() => EMPTY),
              ),
        ),
      )
      .subscribe((unread) => {
        // A message that arrived while the count was on its way may or may not be in it.
        // Asked again now, it surely is: the API sends a message only once it's saved.
        if (this.receivedWhileCounting) {
          this.refreshUnread();
        } else {
          this.unreadSignal.set(unread);
        }
      });

    // Not while a temporary password must be replaced: the API refuses the inbox then too.
    effect(
      () => {
        const signedIn = this.auth.isAuthenticated() && !this.auth.passwordChangeRequired();
        untracked(() => this.counts.next(signedIn ? 'load' : 'clear'));
      },
      { injector: this.injector },
    );

    this.hub.inboxMessage$.subscribe((message) => this.receive(message));
    // Messages sent while the connection was down never arrive; the count catches up.
    this.hub.reconnected$.subscribe(() => this.refreshUnread());
  }

  /** The newest messages, read or not, newest first. */
  list(): Observable<InboxMessage[]> {
    return this.http.get<InboxMessage[]>(this.baseUrl);
  }

  /** Asks the API how many messages are unread, e.g. when the inbox is opened. */
  refreshUnread(): void {
    this.counts.next('load');
  }

  /** Marks one of the user's messages read. Doing it twice is harmless. */
  markRead(message: InboxMessage): Observable<void> {
    return this.http
      .post<void>(`${this.baseUrl}/${encodeURIComponent(message.id)}/read`, null)
      .pipe(
        tap(() => {
          if (message.readAt === null) {
            this.unreadSignal.update((count) => Math.max(0, count - 1));
          }
        }),
      );
  }

  markAllRead(): Observable<void> {
    return this.http
      .post<void>(`${this.baseUrl}/read-all`, null)
      .pipe(tap(() => this.unreadSignal.set(0)));
  }

  private receive(message: InboxMessage): void {
    this.receivedWhileCounting = true;
    this.unreadSignal.update((count) => count + 1);
    this.receivedSubject.next(message);

    const text = inboxSummary(message, this.locale);
    this.notifications.info(text, INBOX_TOAST_MS);

    if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      // The tag makes several open tabs show one notification, not one each.
      const desktop = new Notification($localize`:@@inbox.desktopTitle:New message in SmartDesk`, {
        body: text,
        tag: `inbox-${message.id}`,
      });
      desktop.onclick = () => {
        window.focus();
        void this.router.navigateByUrl('/inbox');
        desktop.close();
      };
    }
  }
}
