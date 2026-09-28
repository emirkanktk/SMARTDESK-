import { PASSWORD_RULES } from '../validators/password.validator';
import { generateTemporaryPassword } from './temporary-password';

describe('generateTemporaryPassword', () => {
  it('always meets every password rule, and avoids characters that are easy to misread', () => {
    for (let i = 0; i < 200; i++) {
      const password = generateTemporaryPassword();

      expect(password).toHaveLength(12);
      for (const rule of PASSWORD_RULES) {
        expect(rule.test(password), `${password} should have ${rule.text}`).toBe(true);
      }
      expect(password).not.toMatch(/[lIO01]/);
    }
  });

  it('differs from one call to the next', () => {
    const seen = new Set(Array.from({ length: 50 }, () => generateTemporaryPassword()));
    expect(seen.size).toBe(50);
  });
});
