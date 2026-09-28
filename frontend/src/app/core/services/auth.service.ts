import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Role, Roles } from '../constants/roles';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import {
  CurrentUser,
  LoginRequest,
  LoginResponse,
  ResetPasswordRequest,
} from '../models/auth.model';
import { JwtClaims, decodeJwtClaims, isJwtExpired } from '../utils/jwt';

const TOKEN_STORAGE_KEY = 'smartdesk_access_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  // Kept in memory and mirrored to storage, so a page reload keeps the user signed in and
  // the session still works for this tab if storage is unavailable.
  private token: string | null = readStoredToken();
  private readonly currentUserSignal = signal<CurrentUser | null>(this.restoreUser());

  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);
  readonly isAdmin = computed(() => this.currentUserSignal()?.roles.includes(Roles.Admin) ?? false);
  readonly displayName = computed(() => {
    const user = this.currentUserSignal();
    return user ? `${user.firstName} ${user.lastName}`.trim() || user.email : '';
  });

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
      .pipe(
        tap((response) => {
          this.token = response.accessToken;
          writeStoredToken(response.accessToken);
          this.currentUserSignal.set(userFromToken(response.accessToken));
        }),
      );
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
    this.token = null;
    removeStoredToken();
    this.currentUserSignal.set(null);
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

function removeStoredToken(): void {
  try {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
  } catch {
    // Nothing stored, nothing to remove.
  }
}
