import { LOCALE_ID } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
import { APP_LANGUAGE, Language, localeFor } from './app/core/i18n/language';

/** Starts the app in a language whose translations main.ts has already loaded. */
export function bootstrap(language: Language) {
  return bootstrapApplication(App, {
    ...appConfig,
    providers: [
      ...appConfig.providers,
      { provide: APP_LANGUAGE, useValue: language },
      { provide: LOCALE_ID, useValue: localeFor(language) },
    ],
  });
}
