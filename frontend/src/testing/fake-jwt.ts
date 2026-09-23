import { JwtClaims } from '../app/core/utils/jwt';

const ONE_HOUR_S = 60 * 60;

/** Builds an unsigned JWT with the claim shape the API issues. Test use only. */
export function fakeJwt(overrides: JwtClaims = {}): string {
  const claims: JwtClaims = {
    sub: '11111111-1111-1111-1111-111111111111',
    email: 'employee1@smartdesk.local',
    given_name: 'Ayşe',
    family_name: 'Yılmaz',
    role: 'Employee',
    exp: Math.floor(Date.now() / 1000) + ONE_HOUR_S,
    ...overrides,
  };
  return `${base64Url({ alg: 'HS256', typ: 'JWT' })}.${base64Url(claims)}.signature`;
}

export function expiredJwt(overrides: JwtClaims = {}): string {
  return fakeJwt({ exp: Math.floor(Date.now() / 1000) - 60, ...overrides });
}

function base64Url(value: object): string {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}
