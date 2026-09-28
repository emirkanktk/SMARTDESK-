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
import { Roles } from '../constants/roles';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { adminGuard, authGuard, guestGuard, managerGuard, sessionGuard } from './auth.guard';

describe('route guards', () => {
  let signedIn: boolean;
  let mustChangePassword: boolean;
  let roles: string[];

  beforeEach(() => {
    signedIn = false;
    mustChangePassword = false;
    roles = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            hasValidSession: () => signedIn,
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

  function serialize(result: MaybeAsync<GuardResult>): string {
    return TestBed.inject(Router).serializeUrl(result as UrlTree);
  }

  describe('authGuard', () => {
    it('allows a signed-in user', () => {
      signedIn = true;
      expect(run(authGuard)).toBe(true);
    });

    it('redirects to login and remembers the requested url', () => {
      expect(serialize(run(authGuard, '/reservations/mine'))).toBe(
        '/login?returnUrl=%2Freservations%2Fmine',
      );
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

    it('sends a signed-out visitor to sign in', () => {
      expect(serialize(run(sessionGuard))).toBe('/login');
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
