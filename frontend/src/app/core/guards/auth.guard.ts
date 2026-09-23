import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role, Roles } from '../constants/roles';
import { NotificationService } from '../services/notification.service';
import { AuthService } from '../services/auth.service';

/** Lets signed-in users through; sends everyone else to login, remembering where they were going. */
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.hasValidSession()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
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

    inject(NotificationService).error('You do not have permission to open that page.');
    return router.parseUrl('/dashboard');
  };
}

export const adminGuard: CanActivateFn = roleGuard(Roles.Admin);
