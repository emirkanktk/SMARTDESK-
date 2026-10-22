import {
  creationOptions,
  credentialJson,
  deviceName,
  fromBase64Url,
  passkeysSupported,
  requestOptions,
  toBase64Url,
} from './webauthn';

describe('webauthn', () => {
  const bytes = (...values: number[]) => new Uint8Array(values).buffer;
  const text = (buffer: BufferSource) =>
    Array.from(new Uint8Array(buffer instanceof ArrayBuffer ? buffer : buffer.buffer));

  afterEach(() => {
    delete (window as { PublicKeyCredential?: unknown }).PublicKeyCredential;
  });

  it('writes and reads base64url, without padding', () => {
    const value = bytes(0xfb, 0xff, 0xbf, 0x01);

    expect(toBase64Url(value)).toBe('-_-_AQ');
    expect(text(fromBase64Url('-_-_AQ'))).toEqual([0xfb, 0xff, 0xbf, 0x01]);
    expect(text(fromBase64Url(toBase64Url(new Uint8Array([1, 2, 3]))))).toEqual([1, 2, 3]);
  });

  it('turns the options JSON into what navigator.credentials takes, where the browser cannot', () => {
    (window as { PublicKeyCredential?: unknown }).PublicKeyCredential = class {};

    const create = creationOptions({
      challenge: 'AQID',
      rp: { id: 'localhost', name: 'SmartDesk' },
      user: { id: 'BAU', name: 'ayse@smartdesk.local', displayName: 'Ayşe Yılmaz' },
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      excludeCredentials: [{ id: 'Bgc', type: 'public-key', transports: ['internal'] }],
      authenticatorSelection: { residentKey: 'required', userVerification: 'required' },
    });
    const get = requestOptions({ challenge: 'CAk', rpId: 'localhost', allowCredentials: [] });

    expect(text(create.challenge)).toEqual([1, 2, 3]);
    expect(text(create.user.id)).toEqual([4, 5]);
    expect(create.user.name).toBe('ayse@smartdesk.local');
    expect(text(create.excludeCredentials![0].id)).toEqual([6, 7]);
    expect(create.excludeCredentials![0].transports).toEqual(['internal']);
    expect(create.authenticatorSelection!.residentKey).toBe('required');
    expect(text(get.challenge)).toEqual([8, 9]);
    expect(get.rpId).toBe('localhost');
  });

  it('leaves the conversion to a browser that has it', () => {
    const parsed = { challenge: bytes(1) } as PublicKeyCredentialRequestOptions;
    const parseRequestOptionsFromJSON = vi.fn(() => parsed);
    (window as { PublicKeyCredential?: unknown }).PublicKeyCredential = Object.assign(class {}, {
      parseRequestOptionsFromJSON,
    });

    expect(requestOptions({ challenge: 'AQ' })).toBe(parsed);
    expect(parseRequestOptionsFromJSON).toHaveBeenCalledWith({ challenge: 'AQ' });
  });

  it("writes a new passkey's credential as toJSON() would", () => {
    const credential = {
      id: 'AQI',
      rawId: bytes(1, 2),
      type: 'public-key',
      authenticatorAttachment: 'platform',
      getClientExtensionResults: () => ({}),
      response: {
        clientDataJSON: bytes(3),
        attestationObject: bytes(4),
        getAuthenticatorData: () => bytes(5),
        getPublicKey: () => bytes(6),
        getPublicKeyAlgorithm: () => -7,
        getTransports: () => ['internal', 'hybrid'],
      },
    } as unknown as PublicKeyCredential;

    expect(credentialJson(credential)).toEqual({
      id: 'AQI',
      rawId: 'AQI',
      type: 'public-key',
      authenticatorAttachment: 'platform',
      clientExtensionResults: {},
      response: {
        clientDataJSON: 'Aw',
        attestationObject: 'BA',
        authenticatorData: 'BQ',
        publicKey: 'Bg',
        publicKeyAlgorithm: -7,
        transports: ['internal', 'hybrid'],
      },
    });
  });

  it("writes a sign-in's credential as toJSON() would, or lets the browser do it", () => {
    const credential = {
      id: 'AQI',
      rawId: bytes(1, 2),
      type: 'public-key',
      authenticatorAttachment: null,
      getClientExtensionResults: () => ({}),
      response: {
        clientDataJSON: bytes(3),
        authenticatorData: bytes(4),
        signature: bytes(5),
        userHandle: bytes(6),
      },
    } as unknown as PublicKeyCredential;

    expect(credentialJson(credential)).toEqual({
      id: 'AQI',
      rawId: 'AQI',
      type: 'public-key',
      authenticatorAttachment: undefined,
      clientExtensionResults: {},
      response: {
        clientDataJSON: 'Aw',
        authenticatorData: 'BA',
        signature: 'BQ',
        userHandle: 'Bg',
      },
    });

    const own = { id: 'own' } as AuthenticationResponseJSON;
    expect(credentialJson({ ...credential, toJSON: () => own } as PublicKeyCredential)).toBe(own);
  });

  it('offers passkeys only in a browser with WebAuthn, on https or localhost', () => {
    const browser = (isSecureContext: boolean, withWebAuthn = true) =>
      ({
        isSecureContext,
        PublicKeyCredential: withWebAuthn ? class {} : undefined,
        navigator: { credentials: { create: () => null, get: () => null } },
      }) as unknown as Window & typeof globalThis;

    expect(passkeysSupported(browser(true))).toBe(true);
    expect(passkeysSupported(browser(false))).toBe(false);
    expect(passkeysSupported(browser(true, false))).toBe(false);
    expect(passkeysSupported(null)).toBe(false);
  });

  it('names a passkey after the kind of device it was made on', () => {
    const on = (userAgent: string, maxTouchPoints = 0) =>
      deviceName({ userAgent, maxTouchPoints } as Navigator);

    expect(on('Mozilla/5.0 (iPhone; CPU iPhone OS 18_6 like Mac OS X) AppleWebKit/605.1.15')).toBe(
      'iPhone',
    );
    expect(on('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15', 5)).toBe(
      'iPad',
    );
    expect(on('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15')).toBe('Mac');
    expect(on('Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36')).toBe('Android');
    expect(on('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36')).toBe('Windows');
    expect(on('Something else')).toBeNull();
  });
});
