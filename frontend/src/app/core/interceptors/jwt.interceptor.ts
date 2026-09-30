import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { isApiUrl } from '../utils/api-url';
import { ANONYMOUS_REQUEST } from './http-context.tokens';

/**
 * Attaches the access token to requests for our own API. A token about to expire - after the
 * laptop slept, say - is renewed first (ADR-0026), so the request doesn't fail for nothing.
 */
export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  // Never hand the token to anything but our own API, and not to anonymous calls (login).
  if (!isApiUrl(req.url) || req.context.get(ANONYMOUS_REQUEST)) {
    return next(req);
  }

  return inject(AuthService)
    .validToken()
    .pipe(
      switchMap((token) =>
        next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req),
      ),
    );
};
