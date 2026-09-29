import { TestBed } from '@angular/core/testing';
import { THEME_STORAGE_KEY, ThemeService } from './theme.service';

describe('ThemeService', () => {
  const root = document.documentElement;
  let systemListener: ((event: MediaQueryListEvent) => void) | null;

  beforeEach(() => {
    systemListener = null;
    // jsdom has no matchMedia: stand in for the system's light/dark setting.
    window.matchMedia = vi.fn().mockReturnValue({
      matches: false,
      addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) =>
        (systemListener = listener),
      removeEventListener: vi.fn(),
    });
  });

  afterEach(() => {
    root.removeAttribute('data-theme');
    localStorage.clear();
    // Not part of jsdom; left in place it would outlive this spec.
    delete (window as { matchMedia?: unknown }).matchMedia;
  });

  const create = () => TestBed.inject(ThemeService);

  it('starts with the theme index.html already applied', () => {
    root.setAttribute('data-theme', 'dark');

    expect(create().theme()).toBe('dark');
  });

  it('switches, and remembers the choice in this browser', () => {
    root.setAttribute('data-theme', 'light');
    const themes = create();

    themes.toggle();
    expect(themes.theme()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');

    themes.toggle();
    expect(root.getAttribute('data-theme')).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  it('follows another tab that switched', () => {
    const themes = create();

    window.dispatchEvent(new StorageEvent('storage', { key: THEME_STORAGE_KEY, newValue: 'dark' }));
    expect(themes.theme()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');

    window.dispatchEvent(new StorageEvent('storage', { key: 'other', newValue: 'light' }));
    expect(themes.theme()).toBe('dark');
  });

  it('follows the system until someone chooses', () => {
    const themes = create();

    systemListener!({ matches: true } as MediaQueryListEvent);
    expect(themes.theme()).toBe('dark');

    themes.toggle(); // light, and now a choice
    systemListener!({ matches: true } as MediaQueryListEvent);
    expect(themes.theme()).toBe('light');
  });

  it('still switches when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const themes = create();

    expect(() => themes.toggle()).not.toThrow();
    expect(themes.theme()).toBe('dark');
    vi.restoreAllMocks();
  });
});
