import { HttpClient, HttpContext } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
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

const TOKEN_STORAGE_KEY = 'smartdesk_access_token';
/** Set while the password is one someone else chose; the token itself doesn't say so. */
const PASSWORD_CHANGE_STORAGE_KEY = 'smartdesk_password_change_required';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  // Kept in memory and mirrored to storage, so a page reload keeps the user signed in and
  // the session still works for this tab if storage is unavailable.
  private token: string | null = readStoredToken();
  private readonly currentUserSignal = signal<CurrentUser | null>(this.restoreUser());

  readonly currentUser = this.currentUserSignal.asReadonly();
  private readonly passwordChangeRequiredSignal = signal(
    this.token !== null && readStored(PASSWORD_CHANGE_STORAGE_KEY) === 'true',
  );
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
    // Every tab of the app shares one stored session. When another tab stores a new one - it
    // signed in again or changed the password - this tab switches to it too.
    const onStorage = (event: StorageEvent) => {
      if (event.key === TOKEN_STORAGE_KEY) {
        this.adoptStoredSession();
      }
    };
    globalThis.addEventListener?.('storage', onStorage);
    inject(DestroyRef).onDestroy(() => globalThis.removeEventListener?.('storage', onStorage));
  }

  /** The access token, or null when there is none or it has expired. */
  getToken(): string | null {
    if (!this.token) {
      return null;
    }

    const claims = decodeJwtClaims(this.token);
    if (!claims || isJwtExpired(claims)) {
      this.logout();
      return null;
    }

    return this.token;
  }

  /** True when there is an unexpired token; clears the session if it has expired. */
  hasValidSession(): boolean {
    return this.getToken() !== null;
  }

  hasRole(role: Role): boolean {
    return this.currentUserSignal()?.roles.includes(role) ?? false;
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/login`, request, {
        context: this.anonymousContext(),
      })
      .pipe(tap((response) => this.startSession(response)));
  }

  /**
   * Replaces the signed-in user's password. Every other session ends, so the answer carries a
   * new token for this one, which replaces the old.
   */
  changePassword(request: ChangePasswordRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/auth/change-password`, request, {
        context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
      })
      .pipe(tap((response) => this.startSession(response)));
  }

  /** The API refused a request because the password must be changed first. */
  markPasswordChangeRequired(): void {
    this.passwordChangeRequiredSignal.set(true);
    writeStored(PASSWORD_CHANGE_STORAGE_KEY, 'true');
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

  logout(): void {
    // Only clear what this tab stored. Another tab may have stored a newer session's token by
    // now - after changing the password there, say, which ends this tab's old session.
    if (readStoredToken() === this.token) {
      removeStoredToken();
      removeStored(PASSWORD_CHANGE_STORAGE_KEY);
    }
    this.token = null;
    this.passwordChangeRequiredSignal.set(false);
    this.currentUserSignal.set(null);
  }

  /**
   * Takes over a session another tab stored, when it's newer than this tab's and still valid:
   * after a password change there, this tab's own token no longer works.
   */
  private adoptStoredSession(): void {
    const stored = readStoredToken();
    const user = stored ? userFromToken(stored) : null;
    if (!stored || !user || stored === this.token) {
      return;
    }
    this.token = stored;
    this.currentUserSignal.set(user);
    this.passwordChangeRequiredSignal.set(readStored(PASSWORD_CHANGE_STORAGE_KEY) === 'true');
  }

  private startSession(response: LoginResponse): void {
    this.token = response.accessToken;
    writeStoredToken(response.accessToken);
    this.currentUserSignal.set(userFromToken(response.accessToken));
    const mustChange = response.passwordChangeRequired === true;
    this.passwordChangeRequiredSignal.set(mustChange);
    if (mustChange) {
      writeStored(PASSWORD_CHANGE_STORAGE_KEY, 'true');
    } else {
      removeStored(PASSWORD_CHANGE_STORAGE_KEY);
    }
  }
  // Sent without a session, so a 401 means "wrong password" rather than "session ended"; and
  // these screens show their own error messages, so no toast on top.
  private anonymousContext(): HttpContext {
    return new HttpContext().set(ANONYMOUS_REQUEST, true).set(SKIP_ERROR_NOTIFICATION, true);
  }

  private restoreUser(): CurrentUser | null {
    const user = this.token ? userFromToken(this.token) : null;
    if (!user) {
      this.token = null;
      removeStoredToken();
    }
    return user;
  }
}

function userFromToken(token: string): CurrentUser | null {
  const claims = decodeJwtClaims(token);
  if (!claims?.sub || isJwtExpired(claims)) {
    return null;
  }

  return {
    id: claims.sub,
    firstName: claims.given_name ?? '',
    lastName: claims.family_name ?? '',
    email: claims.email ?? '',
    roles: rolesFromClaims(claims),
  };
}

function rolesFromClaims(claims: JwtClaims): string[] {
  if (Array.isArray(claims.role)) {
    return claims.role;
  }
  return claims.role ? [claims.role] : [];
}

// Storage can throw (private browsing, blocked site data); treat that as "nothing stored".
function readStoredToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStoredToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
  } catch {
    // The in-memory token keeps this tab signed in; only reload persistence is lost.
  }
}

function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Kept in memory for this tab.
  }
}

function removeStored(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing stored, nothing to remove.
  }
}

function removeStoredToken(): void {
  try {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
  } catch {
    // Nothing stored, nothing to remove.
  }
}
