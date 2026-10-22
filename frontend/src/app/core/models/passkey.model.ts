/** One of the signed-in user's passkeys (ADR-0057), as `GET /api/auth/passkeys` lists them. */
export interface Passkey {
  /** The credential's id, base64url. */
  id: string;
  name: string;
  createdAt: string;
  /** Kept by a password manager that copies it to the owner's other devices, such as iCloud Keychain. */
  synced: boolean;
}

/** A ceremony's options, as WebAuthn's JSON, and the token that goes back with the answer. */
export interface PasskeyOptions<T> {
  options: T;
  token: string;
}
