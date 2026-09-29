import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { map } from 'rxjs';
import { Role, Roles } from '../constants/roles';
import { NotificationService } from '../services/notification.service';
import { AuthService } from '../services/auth.service';

/**
 * Lets signed-in users through; sends everyone else to login, remembering where they were going.
 * A user whose password was set by someone else goes to change it first - the API answers
 * nothing else until then (ADR-0021). A token that just expired - the laptop slept - is renewed
 * from the session first (ADR-0026).
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const signedIn = (): boolean | UrlTree =>
    authService.passwordChangeRequired() ? router.parseUrl('/change-password') : true;

  if (authService.hasValidSession()) {
    return signedIn();
  }
  return authService
    .validToken()
    .pipe(
      map((token) =>
        token
          ? signedIn()
          : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }),
      ),
    );
};

/** For the change-password page itself: any signed-in user, whatever their password state. */
export const sessionGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.hasValidSession()) {
    return true;
  }
  return authService.validToken().pipe(map((token) => (token ? true : router.parseUrl('/login'))));
};

/** Keeps signed-in users off the login screen. */
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.hasValidSession() ? router.parseUrl('/dashboard') : true;
};

/**
 * Lets users with at least one of the given roles through. Use after authGuard. This only
 * hides screens - the API enforces the same roles on every request.
 */
export function roleGuard(...roles: Role[]): CanActivateFn {
  return () => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (roles.some((role) => authService.hasRole(role))) {
      return true;
    }

    inject(NotificationService).error(
      $localize`:@@error.pageForbidden:You do not have permission to open that page.`,
    );
    return router.parseUrl('/dashboard');
  };
}

export const adminGuard: CanActivateFn = roleGuard(Roles.Admin);

export const managerGuard: CanActivateFn = roleGuard(Roles.Manager);

/** The resource forms: admins, and managers for their own team's resources (ADR-0022). */
export const resourceEditorGuard: CanActivateFn = roleGuard(Roles.Admin, Roles.Manager);
