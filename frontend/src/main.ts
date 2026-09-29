import { registerLocaleData } from '@angular/common';
import localeTr from '@angular/common/locales/tr';
import { loadTranslations } from '@angular/localize';
import { Language, storedLanguage } from './app/core/i18n/language';

// Picks the language, loads its translations, and only then imports the app: a $localize
// string at module level is translated when its module is first imported, so the app must not
// be imported before loadTranslations(). The source language is English, so English needs no
// file.
async function start(): Promise<void> {
  let language: Language = storedLanguage();
  if (language === 'tr') {
    try {
      const response = await fetch('i18n/tr.json');
      const file = (await response.json()) as { translations: Record<string, string> };
      loadTranslations(file.translations);
      registerLocaleData(localeTr);
    } catch (error) {
      // Better an English app than none.
      console.error('Could not load the Turkish translations.', error);
      language = 'en';
    }
  }
  document.documentElement.lang = language;

  const { bootstrap } = await import('./bootstrap');
  await bootstrap(language);
}

start().catch((err) => console.error(err));
