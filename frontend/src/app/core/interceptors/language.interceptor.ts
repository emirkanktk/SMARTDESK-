import { HttpInterceptorFn } from '@angular/common/http';
import { LOCALE_ID, inject } from '@angular/core';
import { isApiUrl } from '../utils/api-url';

/**
 * Asks the API to answer in the app's own language (ADR-0023), not the browser's: an English
 * screen gets English errors even in a Turkish browser, and the other way round. The API also
 * saves it at sign-in, for the emails it sends that user later.
 */
export const languageInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiUrl(req.url)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'Accept-Language': inject(LOCALE_ID) } }));
};
