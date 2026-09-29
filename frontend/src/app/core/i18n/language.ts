import { InjectionToken } from '@angular/core';

// The UI language (#58). Turkish unless the user picked English; the choice is remembered per
// browser. Kept free of $localize and of the app's imports: main.ts reads it before the
// translations - and so the app - are loaded.

export type Language = 'tr' | 'en';

export const DEFAULT_LANGUAGE: Language = 'tr';

const STORAGE_KEY = 'smartdesk_language';

/** The language the app was started in. Tests get English, the language of the source. */
export const APP_LANGUAGE = new InjectionToken<Language>('APP_LANGUAGE', {
  providedIn: 'root',
  factory: () => 'en',
});

/** The stored choice, or the default when there is none (or storage is unavailable). */
export function storedLanguage(): Language {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'en' || stored === 'tr' ? stored : DEFAULT_LANGUAGE;
  } catch {
    return DEFAULT_LANGUAGE;
  }
}

/** Remembers the choice. The caller reloads: translations are loaded once, at start. */
export function storeLanguage(language: Language): void {
  try {
    localStorage.setItem(STORAGE_KEY, language);
  } catch {
    // Not remembered; the reload falls back to the default.
  }
}

/** The Angular locale that formats dates and numbers for a language. */
export function localeFor(language: Language): string {
  return language === 'tr' ? 'tr' : 'en-US';
}
