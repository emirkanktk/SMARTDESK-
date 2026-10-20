import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

/** Also read by the script in index.html, which applies the theme before anything is drawn. */
export const THEME_STORAGE_KEY = 'smartdesk_theme';

/**
 * The colour a phone gives its status bar and the browser its toolbar (`theme-color`): the top
 * bar's surface in each theme (--color-surface, styles.scss), so they run into each other
 * (ADR-0055). index.html sets the first one too.
 */
export const THEME_COLORS: Record<Theme, string> = { light: '#ffffff', dark: '#111727' };

/**
 * The colour theme. index.html puts `data-theme` on <html> before the app starts: the stored
 * choice, or the system's setting when there is none. This keeps it: switching stores the
 * choice for this browser, other open tabs follow it, and until someone chooses, the app
 * follows the system as it changes.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly root = this.document.documentElement;
  private readonly themeSignal = signal<Theme>(
    this.root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light',
  );
  readonly theme = this.themeSignal.asReadonly();

  constructor() {
    const view = this.document.defaultView;
    this.paintBars(this.themeSignal());

    const onStorage = (event: StorageEvent) => {
      if (event.key === THEME_STORAGE_KEY && isTheme(event.newValue)) {
        this.apply(event.newValue);
      }
    };
    const system = view?.matchMedia?.('(prefers-color-scheme: dark)');
    const onSystemChange = (event: MediaQueryListEvent) => {
      if (!storedTheme()) {
        this.apply(event.matches ? 'dark' : 'light');
      }
    };

    view?.addEventListener('storage', onStorage);
    system?.addEventListener('change', onSystemChange);
    inject(DestroyRef).onDestroy(() => {
      view?.removeEventListener('storage', onStorage);
      system?.removeEventListener('change', onSystemChange);
    });
  }

  /** Switches to the other theme and remembers it in this browser. */
  toggle(): void {
    const next: Theme = this.themeSignal() === 'dark' ? 'light' : 'dark';
    this.apply(next);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      // Not remembered; the next visit follows the system again.
    }
  }

  private apply(theme: Theme): void {
    this.root.setAttribute('data-theme', theme);
    this.themeSignal.set(theme);
    this.paintBars(theme);
  }

  private paintBars(theme: Theme): void {
    this.document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute('content', THEME_COLORS[theme]);
  }
}

function isTheme(value: string | null): value is Theme {
  return value === 'light' || value === 'dark';
}

function storedTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return isTheme(stored) ? stored : null;
  } catch {
    return null;
  }
}
