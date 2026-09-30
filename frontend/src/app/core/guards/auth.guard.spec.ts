import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  GuardResult,
  MaybeAsync,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';
import { Roles } from '../constants/roles';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { adminGuard, authGuard, guestGuard, managerGuard, sessionGuard } from './auth.guard';

describe('route guards', () => {
  let signedIn: boolean;
  /** Whether the session can still give a new token when the current one has expired. */
  let renewable: boolean;
  /** Someone is signed in here, though a renewal may have got no answer. */
  let known: boolean;
  let mustChangePassword: boolean;
  let roles: string[];

  beforeEach(() => {
    signedIn = false;
    renewable = false;
    known = false;
    mustChangePassword = false;
    roles = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            hasValidSession: () => signedIn,
            validToken: () => of(renewable ? 'renewed-token' : null),
            isAuthenticated: () => known,
            passwordChangeRequired: () => mustChangePassword,
            hasRole: (role: string) => roles.includes(role),
          },
        },
      ],
    });
  });

  function run(guard: CanActivateFn, url = '/dashboard'): MaybeAsync<GuardResult> {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  /** The guard's answer, waited for when it had to renew the session first. */
  async function settle(result: MaybeAsync<GuardResult>): Promise<GuardResult> {
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  function serialize(result: GuardResult | MaybeAsync<GuardResult>): string {
    return TestBed.inject(Router).serializeUrl(result as UrlTree);
  }

  describe('authGuard', () => {
    it('allows a signed-in user', () => {
      signedIn = true;
      expect(run(authGuard)).toBe(true);
    });

    it('redirects to login and remembers the requested url', async () => {
      expect(serialize(await settle(run(authGuard, '/reservations/mine')))).toBe(
        '/login?returnUrl=%2Freservations%2Fmine',
      );
    });

    it('lets a user in whose token expired but whose session goes on', async () => {
      renewable = true;
      expect(await settle(run(authGuard))).toBe(true);
    });

    it("doesn't send anyone to sign in when the renewal got no answer", async () => {
      // The token expired and the server can't be reached: the session isn't known to be over.
      known = true;
      expect(await settle(run(authGuard, '/calendar'))).toBe(true);
    });

    it('sends a user with a temporary password to change it first', () => {
      signedIn = true;
      mustChangePassword = true;
      expect(serialize(run(authGuard, '/calendar'))).toBe('/change-password');
    });
  });

  describe('sessionGuard', () => {
    it('lets any signed-in user change their password, a pending change included', () => {
      signedIn = true;
      mustChangePassword = true;
      expect(run(sessionGuard)).toBe(true);
    });

    it('sends a signed-out visitor to sign in', async () => {
      expect(serialize(await settle(run(sessionGuard)))).toBe('/login');
    });

    it('renews an expired token before deciding', async () => {
      renewable = true;
      expect(await settle(run(sessionGuard))).toBe(true);
    });

    it('lets a signed-in user through when the renewal got no answer', async () => {
      known = true;
      expect(await settle(run(sessionGuard))).toBe(true);
    });
  });

  describe('guestGuard', () => {
    it('allows a signed-out user onto the login screen', () => {
      expect(run(guestGuard)).toBe(true);
    });

    it('sends a signed-in user to the dashboard', () => {
      signedIn = true;
      expect(serialize(run(guestGuard))).toBe('/dashboard');
    });
  });

  describe('adminGuard', () => {
    it('allows an admin', () => {
      roles = [Roles.Admin];
      expect(run(adminGuard)).toBe(true);
    });

    it('sends an employee to the dashboard with a message', () => {
      roles = [Roles.Employee];
      const notifications = TestBed.inject(NotificationService);

      expect(serialize(run(adminGuard))).toBe('/dashboard');
      expect(notifications.notifications()[0]?.kind).toBe('error');
    });
  });

  describe('managerGuard', () => {
    it('allows a manager, and no one else', () => {
      roles = [Roles.Manager];
      expect(run(managerGuard)).toBe(true);

      roles = [Roles.Admin];
      expect(serialize(run(managerGuard))).toBe('/dashboard');
    });
  });
});
