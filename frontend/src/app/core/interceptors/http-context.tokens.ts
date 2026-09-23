import { HttpContextToken } from '@angular/common/http';

/**
 * Set to true on a request whose caller shows its own error message, so errorInterceptor
 * doesn't raise a toast for it as well. Session expiry (401) is still handled.
 */
export const SKIP_ERROR_NOTIFICATION = new HttpContextToken<boolean>(() => false);
