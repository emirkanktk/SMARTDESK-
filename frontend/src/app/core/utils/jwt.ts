// Claims issued by the API's JwtTokenGenerator (see docs/API_CONTRACTS.md, Auth).
export interface JwtClaims {
  sub?: string;
  email?: string;
  given_name?: string;
  family_name?: string;
  // A single role is serialized as a string, several as an array.
  role?: string | string[];
  exp?: number;
}

/**
 * Reads the payload of a JWT without verifying its signature. Only use the result for UI
 * decisions (what to show, where to route) - the API verifies the token on every request.
 */
export function decodeJwtClaims(token: string): JwtClaims | null {
  const payload = token.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='));
    // atob yields Latin-1; decode the bytes as UTF-8 so names like "Ayşe" survive.
    const json = new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0)));
    const claims: unknown = JSON.parse(json);
    return typeof claims === 'object' && claims !== null ? (claims as JwtClaims) : null;
  } catch {
    return null;
  }
}

export function isJwtExpired(claims: JwtClaims, now: number = Date.now()): boolean {
  return claims.exp === undefined || claims.exp * 1000 <= now;
}
