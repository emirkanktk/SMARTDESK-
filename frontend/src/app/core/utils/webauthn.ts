/**
 * Passkeys through the browser's WebAuthn API (ADR-0057), in the JSON the API speaks: options in,
 * as WebAuthn Level 3 writes them, and the credential out, as its toJSON() does. Browsers with
 * PublicKeyCredential.parseCreationOptionsFromJSON and toJSON() convert themselves; for the ones
 * before them - and password managers that stand in for navigator.credentials - it's done here.
 */

/** Whether this page can make and use passkeys: a browser with WebAuthn, on https or localhost. */
export function passkeysSupported(win: (Window & typeof globalThis) | null | undefined): boolean {
  return (
    !!win?.isSecureContext &&
    typeof win.PublicKeyCredential === 'function' &&
    typeof win.navigator?.credentials?.create === 'function'
  );
}

/** What navigator.credentials.create() takes, from the options' JSON. */
export function creationOptions(
  json: PublicKeyCredentialCreationOptionsJSON,
): PublicKeyCredentialCreationOptions {
  if (typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function') {
    return PublicKeyCredential.parseCreationOptionsFromJSON(json);
  }
  return {
    ...json,
    challenge: fromBase64Url(json.challenge),
    user: { ...json.user, id: fromBase64Url(json.user.id) },
    excludeCredentials: json.excludeCredentials?.map(descriptor),
  } as PublicKeyCredentialCreationOptions;
}

/** What navigator.credentials.get() takes, from the options' JSON. */
export function requestOptions(
  json: PublicKeyCredentialRequestOptionsJSON,
): PublicKeyCredentialRequestOptions {
  if (typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function') {
    return PublicKeyCredential.parseRequestOptionsFromJSON(json);
  }
  return {
    ...json,
    challenge: fromBase64Url(json.challenge),
    allowCredentials: json.allowCredentials?.map(descriptor),
  } as PublicKeyCredentialRequestOptions;
}

/** The credential the browser gave, as PublicKeyCredential.toJSON() writes it. */
export function credentialJson(
  credential: PublicKeyCredential,
): RegistrationResponseJSON | AuthenticationResponseJSON {
  if (typeof credential.toJSON === 'function') {
    return credential.toJSON();
  }

  const common = {
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
    clientExtensionResults: credential.getClientExtensionResults(),
  };
  if ('attestationObject' in credential.response) {
    const attestation = credential.response as AuthenticatorAttestationResponse;
    const publicKey = attestation.getPublicKey?.();
    return {
      ...common,
      response: {
        clientDataJSON: toBase64Url(attestation.clientDataJSON),
        attestationObject: toBase64Url(attestation.attestationObject),
        authenticatorData: toBase64Url(attestation.getAuthenticatorData?.() ?? new ArrayBuffer(0)),
        publicKey: publicKey ? toBase64Url(publicKey) : undefined,
        publicKeyAlgorithm: attestation.getPublicKeyAlgorithm?.(),
        transports: attestation.getTransports?.() ?? [],
      },
    } as RegistrationResponseJSON;
  }

  const assertion = credential.response as AuthenticatorAssertionResponse;
  return {
    ...common,
    response: {
      clientDataJSON: toBase64Url(assertion.clientDataJSON),
      authenticatorData: toBase64Url(assertion.authenticatorData),
      signature: toBase64Url(assertion.signature),
      userHandle: assertion.userHandle ? toBase64Url(assertion.userHandle) : undefined,
    },
  } as AuthenticationResponseJSON;
}

/**
 * A name for a passkey made here until its owner gives a better one: the kind of device, which
 * is what tells two of them apart in the list.
 */
export function deviceName(nav: Navigator): string | null {
  const agent = nav.userAgent;
  if (/iPhone/.test(agent)) return 'iPhone';
  // An iPad says it's a Mac, but has a touch screen.
  if (/iPad/.test(agent) || (/Macintosh/.test(agent) && nav.maxTouchPoints > 1)) return 'iPad';
  if (/Android/.test(agent)) return 'Android';
  if (/Macintosh/.test(agent)) return 'Mac';
  if (/Windows/.test(agent)) return 'Windows';
  if (/CrOS/.test(agent)) return 'Chromebook';
  if (/Linux/.test(agent)) return 'Linux';
  return null;
}

export function toBase64Url(buffer: ArrayBuffer | ArrayBufferView): string {
  const bytes =
    buffer instanceof ArrayBuffer
      ? new Uint8Array(buffer)
      : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fromBase64Url(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '='));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

function descriptor(json: PublicKeyCredentialDescriptorJSON): PublicKeyCredentialDescriptor {
  return { ...json, id: fromBase64Url(json.id) } as PublicKeyCredentialDescriptor;
}
