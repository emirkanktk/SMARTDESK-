import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // jwtInterceptor attaches the token; errorInterceptor maps failures and ends the session
    // on a 401 (except for requests marked ANONYMOUS_REQUEST, i.e. login).
    provideHttpClient(withInterceptors([jwtInterceptor, errorInterceptor])),
  ],
};
