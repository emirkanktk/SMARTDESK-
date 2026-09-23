import { HttpContextToken } from '@angular/common/http';

/**
 * Set to true on a request whose caller shows its own error message, so errorInterceptor
 * doesn't raise a toast for it as well. Session expiry (401) is still handled.
 */
export const SKIP_ERROR_NOTIFICATION = new HttpContextToken<boolean>(() => false);

/**
 * Set to true on a request that is made without a session (login). jwtInterceptor sends it
 * without a token, and errorInterceptor treats its 401 as a normal failure (wrong password)
 * instead of "your session has ended". Every other 401 from the API ends the session.
 */
export const ANONYMOUS_REQUEST = new HttpContextToken<boolean>(() => false);
