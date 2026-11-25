/** How a sign-in session began (ADR-0061). */
export type SignInMethod = 'Password' | 'Passkey';

/**
 * One of the devices the user is signed in on (GET /api/auth/sessions, ADR-0061). `userAgent` and
 * `method` are null for a session from before they were noted. `lastActiveAt`: its sign-in or its
 * latest renewal, which an open app makes about every quarter of an hour. `current`: this device.
 */
export interface SignInSession {
  id: string;
  userAgent: string | null;
  method: SignInMethod | null;
  startedAt: string;
  lastActiveAt: string;
  current: boolean;
}

/** What signing out everywhere else did. */
export interface EndedSessions {
  ended: number;
}
