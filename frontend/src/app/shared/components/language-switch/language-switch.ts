import { Component, inject } from '@angular/core';
import { APP_LANGUAGE, Language, storeLanguage } from '../../../core/i18n/language';

/** Reloads the page; translations are loaded once, at start. Swapped out in tests. */
export const reloadPage = { run: () => location.reload() };

/**
 * TR | EN, for the top bar, the front page and the sign-in page (#58). Each language is named
 * in itself, with its own `lang`, so a screen reader pronounces it right whatever the page's
 * language is. Choosing one stores it and reloads.
 */
@Component({
  selector: 'app-language-switch',
  template: `
    <div class="language-switch" role="group" aria-label="Language / Dil">
      @for (option of options; track option.code) {
        <button
          type="button"
          [attr.lang]="option.code"
          [attr.aria-pressed]="option.code === current"
          [attr.aria-label]="option.name"
          [title]="option.name"
          [class.active]="option.code === current"
          (click)="choose(option.code)"
        >
          {{ option.short }}
        </button>
      }
    </div>
  `,
  styles: `
    .language-switch {
      display: inline-flex;
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      overflow: hidden;
    }
    button {
      padding: var(--space-1) var(--space-2);
      border: 0;
      background: var(--color-surface);
      color: var(--color-text-muted);
      font: inherit;
      font-size: 0.8125rem;
      font-weight: 600;
      cursor: pointer;
    }
    button + button {
      border-left: 1px solid var(--color-border);
    }
    button.active {
      background: var(--color-primary-soft);
      color: var(--color-primary);
    }
  `,
})
export class LanguageSwitch {
  protected readonly current = inject(APP_LANGUAGE);
  protected readonly options: readonly { code: Language; short: string; name: string }[] = [
    { code: 'tr', short: 'TR', name: 'Türkçe' },
    { code: 'en', short: 'EN', name: 'English' },
  ];

  protected choose(language: Language): void {
    if (language === this.current) {
      return;
    }
    storeLanguage(language);
    reloadPage.run();
  }
}
