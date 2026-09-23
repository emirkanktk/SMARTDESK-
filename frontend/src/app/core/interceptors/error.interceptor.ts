import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ApiError } from '../models/api-error.model';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { SKIP_ERROR_NOTIFICATION } from './http-context.tokens';

/**
 * Turns every failed API call into an ApiError and handles the cases no single screen
 * should have to: an expired session, a forbidden action, an unreachable or failing server.
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
      const notify = !req.context.get(SKIP_ERROR_NOTIFICATION);

      // Only a request that carried a token can mean "your session ended"; a 401 from the
      // login endpoint is just a wrong password.
      if (apiError.status === 401 && req.headers.has('Authorization')) {
        authService.logout();
        notifications.info('Your session has expired. Please sign in again.');
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      } else if (notify) {
        const message = messageFor(apiError);
        if (message) {
          notifications.error(message);
        }
      }

      return throwError(() => apiError);
    }),
  );
};

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
