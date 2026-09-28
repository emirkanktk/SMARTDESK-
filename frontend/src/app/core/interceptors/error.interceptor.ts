import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ApiError } from '../models/api-error.model';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { isApiUrl } from '../utils/api-url';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from './http-context.tokens';

const SESSION_ENDED_MESSAGE = 'Your session has expired. Please sign in again.';
/** The ProblemDetails code the API sends while the password must be changed first. */
const PASSWORD_CHANGE_REQUIRED = 'password-change-required';

/**
 * Turns every failed API call into an ApiError and handles the cases no single screen
 * should have to: an ended session, a forbidden action, an unreachable or failing server.
 * 400 and 409 are left to the caller - they carry form- or business-rule-specific messages.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const notifications = inject(NotificationService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const apiError = ApiError.fromHttpError(error);

      // A 401 from the API means the session is over - expired, revoked, or the token was
      // already dropped as expired before the request went out. Only anonymous calls
      // (login) get a 401 for another reason: a wrong password.
      if (apiError.status === 401 && isApiUrl(req.url) && !req.context.get(ANONYMOUS_REQUEST)) {
        endSession(authService, notifications, router);
      } else if (apiError.status === 403 && apiError.code === PASSWORD_CHANGE_REQUIRED) {
        // Someone set a temporary password on this account (ADR-0021): nothing else works
        // until it's replaced, so go there instead of showing "not allowed".
        authService.markPasswordChangeRequired();
        void router.navigateByUrl('/change-password');
      } else if (!req.context.get(SKIP_ERROR_NOTIFICATION)) {
        const message = messageFor(apiError);
        if (message) {
          notifications.error(message);
        }
      }

      return throwError(() => apiError);
    }),
  );
};

/**
 * Signs the user out and sends them to login. Safe to call for each of several requests
 * that fail together: the toast is de-duplicated by NotificationService, and only the first
 * call navigates.
 */
function endSession(
  authService: AuthService,
  notifications: NotificationService,
  router: Router,
): void {
  authService.logout();
  notifications.info(SESSION_ENDED_MESSAGE);

  if (isOnOrGoingToLogin(router)) {
    return;
  }
  void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
}

function isOnOrGoingToLogin(router: Router): boolean {
  const navigation = router.currentNavigation();
  const target = navigation
    ? router.serializeUrl(navigation.finalUrl ?? navigation.extractedUrl)
    : router.url;
  return target === '/login' || target.startsWith('/login?');
}

function messageFor(error: ApiError): string | null {
  switch (true) {
    case error.status === 0:
      return error.title;
    case error.status === 403:
      return 'You do not have permission to do that.';
    case error.status === 404:
      return 'The requested item could not be found.';
    case error.status >= 500:
      return 'Something went wrong on the server. Please try again.';
    default:
      return null;
  }
}
