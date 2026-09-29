import { DEFAULT_LANGUAGE, localeFor, storeLanguage, storedLanguage } from './language';

describe('language', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('is Turkish until the user picks English', () => {
    expect(DEFAULT_LANGUAGE).toBe('tr');
    expect(storedLanguage()).toBe('tr');

    storeLanguage('en');
    expect(storedLanguage()).toBe('en');

    storeLanguage('tr');
    expect(storedLanguage()).toBe('tr');
  });

  it('ignores a stored value it does not know', () => {
    localStorage.setItem('smartdesk_language', 'de');

    expect(storedLanguage()).toBe('tr');
  });

  it('falls back to Turkish when storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => storeLanguage('en')).not.toThrow();
    expect(storedLanguage()).toBe('tr');
  });

  it('formats dates and numbers with the matching locale', () => {
    expect(localeFor('tr')).toBe('tr');
    expect(localeFor('en')).toBe('en-US');
  });
});
