/**
 * Makes argument-less toLocaleLowerCase()/toLocaleUpperCase() use Turkish rules, where "I"
 * lowercases to dotless "ı" and "i" uppercases to "İ". ECMA-402 says the argument-less form
 * uses the host's default locale, so that's what a spec-following browser set to Turkish
 * does. V8 (Node, and Chrome/Edge even with a tr-TR locale) doesn't apply it, so without
 * this a locale bug would pass unnoticed in our tests. Call the returned function to undo.
 */
export function simulateTurkishBrowser(): () => void {
  const lower = String.prototype.toLocaleLowerCase;
  const upper = String.prototype.toLocaleUpperCase;
  const lowerSpy = vi.spyOn(String.prototype, 'toLocaleLowerCase').mockImplementation(function (
    this: string,
    locales?: Intl.LocalesArgument,
  ) {
    return lower.call(this, locales ?? 'tr-TR');
  });
  const upperSpy = vi.spyOn(String.prototype, 'toLocaleUpperCase').mockImplementation(function (
    this: string,
    locales?: Intl.LocalesArgument,
  ) {
    return upper.call(this, locales ?? 'tr-TR');
  });
  return () => {
    lowerSpy.mockRestore();
    upperSpy.mockRestore();
  };
}
