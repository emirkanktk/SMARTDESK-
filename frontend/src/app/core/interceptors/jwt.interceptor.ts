import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { isApiUrl } from '../utils/api-url';
import { ANONYMOUS_REQUEST } from './http-context.tokens';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  // Never hand the token to anything but our own API, and not to anonymous calls (login).
  if (!isApiUrl(req.url) || req.context.get(ANONYMOUS_REQUEST)) {
    return next(req);
  }

  const token = inject(AuthService).getToken();

  if (!token) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
