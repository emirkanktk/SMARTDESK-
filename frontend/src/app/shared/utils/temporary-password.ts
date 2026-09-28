const LOWER = 'abcdefghijkmnopqrstuvwxyz';
const UPPER = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
const DIGITS = '23456789';
const SYMBOLS = '-!#$%*+';

/**
 * A random password that meets the API's policy (ADR-0021 temporary passwords): 12 characters
 * with a lowercase and an uppercase letter, a digit and a symbol. Letters and digits that are
 * easy to misread aloud (l, I, O, 0, 1) are left out, since it's handed over in person.
 */
export function generateTemporaryPassword(length = 12): string {
  const pick = (alphabet: string) => alphabet[randomIndex(alphabet.length)];
  const all = LOWER + UPPER + DIGITS + SYMBOLS;
  const chars = [pick(LOWER), pick(UPPER), pick(DIGITS), pick(SYMBOLS)];
  while (chars.length < length) {
    chars.push(pick(all));
  }
  // Fisher-Yates, so the guaranteed classes don't always sit at the start.
  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomIndex(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join('');
}

function randomIndex(max: number): number {
  const value = new Uint32Array(1);
  crypto.getRandomValues(value);
  return value[0] % max;
}
