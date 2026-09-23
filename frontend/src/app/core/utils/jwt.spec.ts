import { expiredJwt, fakeJwt } from '../../../testing/fake-jwt';
import { decodeJwtClaims, isJwtExpired } from './jwt';

describe('decodeJwtClaims', () => {
  it('decodes the payload including non-ASCII names', () => {
    const claims = decodeJwtClaims(fakeJwt({ given_name: 'Ayşe', family_name: 'Yılmaz' }));

    expect(claims?.given_name).toBe('Ayşe');
    expect(claims?.family_name).toBe('Yılmaz');
  });

  it('returns null for a malformed token', () => {
    expect(decodeJwtClaims('not-a-jwt')).toBeNull();
    expect(decodeJwtClaims('a.%%%.c')).toBeNull();
  });
});

describe('isJwtExpired', () => {
  it('is false before exp and true after it', () => {
    expect(isJwtExpired(decodeJwtClaims(fakeJwt())!)).toBe(false);
    expect(isJwtExpired(decodeJwtClaims(expiredJwt())!)).toBe(true);
  });

  it('treats a token without exp as expired', () => {
    expect(isJwtExpired({})).toBe(true);
  });
});
