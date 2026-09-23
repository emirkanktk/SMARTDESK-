import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  // Never hand the token to anything but our own API.
  if (!isApiUrl(req.url)) {
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

function isApiUrl(url: string): boolean {
  return url === environment.apiUrl || url.startsWith(`${environment.apiUrl}/`);
}
