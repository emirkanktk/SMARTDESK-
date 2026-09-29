import { HttpClient, HttpContext } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, from, map, of, shareReplay, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Role, Roles } from '../constants/roles';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { RegisterRequest } from '../models/account.model';
import {
  ChangePasswordRequest,
  CurrentUser,
  LoginRequest,
  LoginResponse,
  ResetPasswordRequest,
} from '../models/auth.model';
import { JwtClaims, decodeJwtClaims, isJwtExpired } from '../utils/jwt';

/** Renew this long before the access token expires, so a request never goes out with a stale one. */
const RENEW_BEFORE_MS = 60_000;
/** A token with less time left than this is renewed before use. */
const FRESH_FOR_MS = 30_000;
/** Tabs of the app tell each other when someone signs in or out. */
const CHANNEL_NAME = 'smartdesk-session';
/** Only one tab renews at a time, as each renewal replaces the shared refresh cookie. */
const RENEW_LOCK = 'smartdesk-session-renew';
/** Where earlier versions kept the token; cleared once, so nothing readable is left behind. */
const LEGACY_STORAGE_KEYS = ['smartdesk_access_token', 'smartdesk_password_change_required'];

type ChannelMessage = 'signed-in' | 'signed-out';

/**
 * The signed-in session (ADR-0026). The access token lives only in memory and lasts minutes;
 * the session itself is an HttpOnly cookie the browser sends to `/api/auth/refresh`, which no
 * script can read. The app renews its token from it at start-up, shortly before the token
 * expires, and whenever the API refuses one, so a user stays signed in for as long as they use
 * the app, and signing out ends the session on the server too.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private token: string | null = null;
  private renewTimer: ReturnType<typeof setTimeout> | null = null;
  private renewing: Observable<string | null> | null = null;
  private readonly channel = openChannel();

  private readonly currentUserSignal = signal<CurrentUser | null>(null);
  readonly currentUser = this.currentUserSignal.asReadonly();
  private readonly passwordChangeRequiredSignal = signal(false);
  /** Until it's changed, the API answers nothing else (ADR-0021); guards send the user there. */
  readonly passwordChangeRequired = this.passwordChangeRequiredSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);
  readonly isAdmin = computed(() => this.currentUserSignal()?.roles.includes(Roles.Admin) ?? false);
  readonly isManager = computed(
    () => this.currentUserSignal()?.roles.includes(Roles.Manager) ?? false,
  );
  readonly displayName = computed(() => {
    const user = this.currentUserSignal();
    return user ? `${user.firstName} ${user.lastName}`.trim() || user.email : '';
  });

  constructor() {
    clearLegacyStorage();

    // Another tab signed in (the shared cookie now holds a new session): pick it up. Another
    // tab signed out: the session is over here too.
    const onMessage = (event: MessageEvent<ChannelMessage>) => {
      if (event.data === 'signed-out') {
        this.clearSession();
      } else if (event.data === 'signed-in') {
        this.renew().subscribe();
      }
    };
    this.channel?.addEventListener('message', onMessage);
    inject(DestroyRef).onDestroy(() => {
      this.channel?.removeEventListener('message', onMessage);
      this.channel?.close();
      this.stopRenewTimer();
    });
  }

  /** At start-up: takes up the session this browser already has, if it has one. */
  restoreSession(): Promise<void> {
    return new Promise((resolve) => this.renew().subscribe({ complete: resolve }));
  }

  /** The access token, or null when there is none or it has expired. */
  getToken(): string | null {
    const claims = this.token ? decodeJwtClaims(this.token) : null;
    return claims && !isJwtExpired(claims) ? this.token : null;
  }

  /** True while there is an unexpired access token. */
  hasValidSession(): boolean {
    return this.getToken() !== null;
  }

  /**
   * A token good for a while yet: the current one, or a renewed one when it's about to expire.
   * Null when the session is over.
   */
  validToken(): Observable<string | null> {
    const claims = this.token ? decodeJwtClaims(this.token) : null;
    return claims && !isJwtExpired(claims, Date.now() + FRESH_FOR_MS)
      ? of(this.token)
      : this.renew();
  }

  /**
   * A new access token from the session cookie; the API replaces the cookie on the way. Calls
   * made while one is under way share it. Null, and signed out here, when the session is over.
   */
  renew(): Observable<string | null> {
    this.renewing ??= from(
      withRenewLock(
        () =>
          new Promise<LoginResponse | null>((resolve) =>
            this.http
              .post<LoginResponse>(`${environment.apiUrl}/auth/refresh`, null, {
                context: this.anonymousContext(),
              })
              .subscribe({ next: resolve, error: () => resolve(null) }),
          ),
      ),
    ).pipe(
      map((response) => {
        if (!response) {
          this.clearSession();
          return null;
        }
        this.startSession(response);
        return response.accessToken;
      }),
      catchError(() => {
        this.clearSession();
        return of(null);
      }),
      finalize(() => (this.renewing = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.renewing;
  }

  hasRole(role: Role): boolean {
    return this.currentUserSignal()?.roles.includes(role) ?? false;
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/login`, request, {
        context: this.anonymousContext(),
      })
      .pipe(tap((response) => this.signedIn(response)));
  }

  /**
   * Replaces the signed-in user's password. Every session ends, so the answer starts a new one
   * for this browser, and the other tabs pick it up.
   */
  changePassword(request: ChangePasswordRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/change-password`, request, {
        context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
      })
      .pipe(tap((response) => this.signedIn(response)));
  }

  /** The API refused a request because the password must be changed first. */
  markPasswordChangeRequired(): void {
    this.passwordChangeRequiredSignal.set(true);
  }

  /**
   * Signs up a Manager, who waits for an Admin, or - with a manager's email - an Employee asking
   * to join that manager's team (ADR-0020, ADR-0021). The API answers the same way whether or
   * not the email already has an account, or the manager's email matched anyone.
   */
  register(request: RegisterRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/auth/register`, request, {
      context: this.anonymousContext(),
    });
  }

  /**
   * Asks for a reset link. The API answers the same way whether or not the email has an
   * account, so the screen can only ever say "if it exists, a link is on its way".
   */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(
      `${environment.apiUrl}/auth/forgot-password`,
      { email },
      { context: this.anonymousContext() },
    );
  }

  /** Sets a new password with the token from the emailed link. */
  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${environment.apiUrl}/auth/reset-password`, request, {
      context: this.anonymousContext(),
    });
  }

  /** Refreshes the profile from the API, which is the source of truth for name and roles. */
  loadCurrentUser(): Observable<CurrentUser> {
    return this.http
      .get<CurrentUser>(`${environment.apiUrl}/auth/me`)
      .pipe(tap((user) => this.currentUserSignal.set(user)));
  }

  /** Signs out everywhere in this browser, and ends the session on the server. */
  logout(): void {
    this.http
      .post<void>(`${environment.apiUrl}/auth/logout`, null, { context: this.anonymousContext() })
      .subscribe({ error: () => undefined });
    this.clearSession();
    this.channel?.postMessage('signed-out' satisfies ChannelMessage);
  }

  private signedIn(response: LoginResponse): void {
    this.startSession(response);
    this.channel?.postMessage('signed-in' satisfies ChannelMessage);
  }

  private startSession(response: LoginResponse): void {
    const claims = decodeJwtClaims(response.accessToken);
    const user = claims ? userFromClaims(claims) : null;
    if (!claims?.exp || !user) {
      this.clearSession();
      return;
    }

    this.token = response.accessToken;
    this.currentUserSignal.set(user);
    this.passwordChangeRequiredSignal.set(response.passwordChangeRequired === true);
    this.scheduleRenewal(claims.exp * 1000);
  }

  private clearSession(): void {
    this.stopRenewTimer();
    this.token = null;
    this.passwordChangeRequiredSignal.set(false);
    this.currentUserSignal.set(null);
  }

  private scheduleRenewal(expiresAtMs: number): void {
    this.stopRenewTimer();
    const delay = Math.max(expiresAtMs - Date.now() - RENEW_BEFORE_MS, 5_000);
    this.renewTimer = setTimeout(() => this.renew().subscribe(), delay);
  }

  private stopRenewTimer(): void {
    if (this.renewTimer !== null) {
      clearTimeout(this.renewTimer);
      this.renewTimer = null;
    }
  }

  // Sent without a token: a 401 here means "no session" or "wrong password", not "session
  // ended mid-use"; and these callers handle their own errors, so no toast on top.
  private anonymousContext(): HttpContext {
    return new HttpContext().set(ANONYMOUS_REQUEST, true).set(SKIP_ERROR_NOTIFICATION, true);
  }
}

function userFromClaims(claims: JwtClaims): CurrentUser | null {
  if (!claims.sub || isJwtExpired(claims)) {
    return null;
  }

  return {
    id: claims.sub,
    firstName: claims.given_name ?? '',
    lastName: claims.family_name ?? '',
    email: claims.email ?? '',
    roles: Array.isArray(claims.role) ? claims.role : claims.role ? [claims.role] : [],
  };
}

/** Runs `work` while holding the renewal lock across tabs, where the browser supports it. */
function withRenewLock<T>(work: () => Promise<T>): Promise<T> {
  const locks = globalThis.navigator?.locks;
  return locks ? locks.request(RENEW_LOCK, work) : work();
}

function openChannel(): BroadcastChannel | null {
  try {
    return typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL_NAME);
  } catch {
    return null;
  }
}

function clearLegacyStorage(): void {
  try {
    for (const key of LEGACY_STORAGE_KEYS) {
      localStorage.removeItem(key);
    }
  } catch {
    // Storage blocked: nothing was stored either.
  }
}
