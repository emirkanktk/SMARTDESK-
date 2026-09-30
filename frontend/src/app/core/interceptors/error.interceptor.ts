import { HttpErrorResponse, HttpEvent, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { OperatorFunction, catchError, switchMap, throwError } from 'rxjs';
import { ApiError, SERVER_UNREACHABLE } from '../models/api-error.model';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { isApiUrl } from '../utils/api-url';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from './http-context.tokens';

const SESSION_ENDED_MESSAGE = $localize`:@@error.sessionEnded:Your session has expired. Please sign in again.`;
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

  // `retried`: the request was already sent again with a renewed token, which happens at most
  // once. The retry goes through this same handling, so its failure is treated like any other.
  const handle = (
    request: HttpRequest<unknown>,
    retried: boolean,
  ): OperatorFunction<HttpEvent<unknown>, HttpEvent<unknown>> =>
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const apiError = ApiError.fromHttpError(error);

      // A 401 from the API means the token is no longer good. Only anonymous calls (login)
      // get a 401 for another reason: a wrong password.
      if (
        apiError.status === 401 &&
        isApiUrl(request.url) &&
        !request.context.get(ANONYMOUS_REQUEST)
      ) {
        // The session may well go on - the token expired while the laptop slept, or another
        // tab changed the password and started a new session (ADR-0026) - so renew once and
        // try again. Only the API saying the session is over signs out; a renewal that got
        // no answer leaves the user signed in, and this request fails like any other that
        // couldn't reach the server.
        if (!retried) {
          const refused = bearerToken(request.headers.get('Authorization'));
          return authService.renew().pipe(
            switchMap((token) => {
              if (token && token !== refused) {
                const retry = request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
                return next(retry).pipe(handle(retry, true));
              }
              if (authService.isAuthenticated()) {
                if (!request.context.get(SKIP_ERROR_NOTIFICATION)) {
                  notifications.error(SERVER_UNREACHABLE);
                }
              } else {
                endSession(authService, notifications, router);
              }
              return throwError(() => apiError);
            }),
          );
        }
        endSession(authService, notifications, router);
      } else if (apiError.status === 403 && apiError.code === PASSWORD_CHANGE_REQUIRED) {
        // Someone set a temporary password on this account (ADR-0021): nothing else works
        // until it's replaced, so go there instead of showing "not allowed".
        authService.markPasswordChangeRequired();
        void router.navigateByUrl('/change-password');
      } else if (!request.context.get(SKIP_ERROR_NOTIFICATION)) {
        const message = messageFor(apiError);
        if (message) {
          notifications.error(message);
        }
      }

      return throwError(() => apiError);
    });

  return next(req).pipe(handle(req, false));
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
      return $localize`:@@error.forbidden:You do not have permission to do that.`;
    case error.status === 404:
      return $localize`:@@error.notFound:The requested item could not be found.`;
    case error.status >= 500:
      return $localize`:@@error.server:Something went wrong on the server. Please try again.`;
    default:
      return null;
  }
}

function bearerToken(header: string | null): string | null {
  return header?.startsWith('Bearer ') ? header.slice('Bearer '.length) : null;
}
