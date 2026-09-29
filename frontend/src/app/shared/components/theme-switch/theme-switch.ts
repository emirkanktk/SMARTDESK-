import { Component, computed, inject } from '@angular/core';
import { ThemeService } from '../../../core/theme/theme.service';

/**
 * Light or dark, next to the language switch in the top bar, on the front page and on
 * sign-in. One button: a moon while the page is light, a sun while it is dark. It's a toggle
 * named "Dark mode", pressed while dark, so a screen reader says what it does and where it is.
 */
@Component({
  selector: 'app-theme-switch',
  template: `
    <button
      type="button"
      class="theme-switch"
      aria-label="Dark mode"
      i18n-aria-label="@@theme.dark"
      [attr.aria-pressed]="dark()"
      [title]="dark() ? toLight : toDark"
      (click)="themes.toggle()"
    >
      @if (dark()) {
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <circle cx="12" cy="12" r="4.5" />
          <path
            d="M12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M4.9 19.1l1.8-1.8M17.3 6.7l1.8-1.8"
          />
        </svg>
      } @else {
        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path d="M20.5 14.5A8.5 8.5 0 0 1 9.5 3.5a8.5 8.5 0 1 0 11 11Z" />
        </svg>
      }
    </button>
  `,
  styles: `
    .theme-switch {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: var(--space-1) var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      color: var(--color-text-muted);
      font: inherit;
      font-size: 0.8125rem;
      line-height: 1.5;
      cursor: pointer;
    }
    .theme-switch:hover {
      color: var(--color-text);
    }
    svg {
      width: 1.5em;
      height: 1.5em;
      fill: none;
      stroke: currentColor;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class ThemeSwitch {
  protected readonly themes = inject(ThemeService);
  protected readonly dark = computed(() => this.themes.theme() === 'dark');
  protected readonly toDark = $localize`:@@theme.toDark:Switch to dark mode`;
  protected readonly toLight = $localize`:@@theme.toLight:Switch to light mode`;
}
