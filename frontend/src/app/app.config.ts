import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';
import { languageInterceptor } from './core/interceptors/language.interceptor';
import { AuthService } from './core/services/auth.service';
import { CheckInTimesService } from './core/services/check-in-times.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // languageInterceptor asks the API for the app's language; jwtInterceptor attaches the
    // token; errorInterceptor maps failures and ends the session on a 401 (except for requests
    // marked ANONYMOUS_REQUEST, i.e. login).
    provideHttpClient(withInterceptors([languageInterceptor, jwtInterceptor, errorInterceptor])),
    // Before the first route: takes up the session this browser already has (ADR-0026).
    provideAppInitializer(() => inject(AuthService).restoreSession()),
    // And reads the check-in times the API uses, since the grace is a setting (ADR-0040).
    provideAppInitializer(() => inject(CheckInTimesService).load()),
  ],
};
