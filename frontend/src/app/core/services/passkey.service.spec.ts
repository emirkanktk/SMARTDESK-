import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST } from '../interceptors/http-context.tokens';
import { AuthService } from './auth.service';
import { PasskeyService } from './passkey.service';

describe('PasskeyService', () => {
  const baseUrl = `${environment.apiUrl}/auth/passkeys`;
  let create: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;
  let passkeySignIn: ReturnType<typeof vi.fn>;
  let http: HttpTestingController;
  const realCredentials = Object.getOwnPropertyDescriptor(navigator, 'credentials');

  /** What the device's authenticator hands back; the browser writes its JSON. */
  const credential = (json: object) => ({ toJSON: () => json }) as unknown as PublicKeyCredential;

  /** jsdom has no WebAuthn: stand in for a browser with it, on localhost. */
  function browserWithPasskeys(): void {
    Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });
    (window as { PublicKeyCredential?: unknown }).PublicKeyCredential = class {};
    Object.defineProperty(navigator, 'credentials', {
      value: { create, get },
      configurable: true,
    });
  }

  beforeEach(() => {
    create = vi.fn();
    get = vi.fn();
    passkeySignIn = vi.fn(() => of({ accessToken: 'jwt', expiresAt: '2026-10-22T10:15:00Z' }));
    browserWithPasskeys();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { passkeySignIn } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    delete (window as { PublicKeyCredential?: unknown }).PublicKeyCredential;
    delete (window as { isSecureContext?: unknown }).isSecureContext;
    if (realCredentials) {
      Object.defineProperty(navigator, 'credentials', realCredentials);
    } else {
      delete (navigator as { credentials?: unknown }).credentials;
    }
  });

  const service = () => TestBed.inject(PasskeyService);
  /** Lets the awaited steps after an answer run before the next request is looked for. */
  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('adds a passkey: the password first, then the device, then the answer with its token', async () => {
    create.mockResolvedValue(credential({ id: 'new-credential' }));

    const added = service().add('Secret123!', 'iPhone');
    const start = http.expectOne(`${baseUrl}/creation-options`);
    expect(start.request.body).toEqual({ password: 'Secret123!' });
    start.flush({
      options: {
        challenge: 'AQID',
        rp: { id: 'localhost', name: 'SmartDesk' },
        user: { id: 'BAU', name: 'ayse@smartdesk.local', displayName: 'Ayşe Yılmaz' },
        pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      },
      token: 'token-1',
    });
    await settle();

    const publicKey = create.mock.calls[0][0].publicKey as PublicKeyCredentialCreationOptions;
    expect(Array.from(new Uint8Array(publicKey.challenge as ArrayBuffer))).toEqual([1, 2, 3]);
    expect(publicKey.rp.id).toBe('localhost');
    const answer = http.expectOne(baseUrl);
    expect(answer.request.method).toBe('POST');
    expect(answer.request.body).toEqual({
      credential: { id: 'new-credential' },
      token: 'token-1',
      name: 'iPhone',
    });
    const passkey = {
      id: 'new-credential',
      name: 'iPhone',
      createdAt: '2026-10-22T10:00:00Z',
      synced: true,
    };
    answer.flush(passkey);

    expect(await added).toEqual(passkey);
  });

  it('signs in with a passkey without a session, and starts one from the answer', async () => {
    get.mockResolvedValue(credential({ id: 'credential-1' }));

    const signedIn = service().signIn();
    const start = http.expectOne(`${baseUrl}/request-options`);
    expect(start.request.context.get(ANONYMOUS_REQUEST)).toBe(true);
    start.flush({
      options: { challenge: 'CAk', rpId: 'localhost', allowCredentials: [] },
      token: 'token-2',
    });

    expect(await signedIn).toEqual({ accessToken: 'jwt', expiresAt: '2026-10-22T10:15:00Z' });
    expect(passkeySignIn).toHaveBeenCalledWith({ id: 'credential-1' }, 'token-2');
  });

  it('takes a prompt that ends without a passkey as the person closing it', async () => {
    get.mockResolvedValue(null);

    const signedIn = service().signIn();
    http
      .expectOne(`${baseUrl}/request-options`)
      .flush({ options: { challenge: 'CAk' }, token: 'token-3' });

    await expect(signedIn).rejects.toMatchObject({ name: 'NotAllowedError' });
    expect(passkeySignIn).not.toHaveBeenCalled();
  });

  it("lists and removes the signed-in user's passkeys", () => {
    service().list().subscribe();
    http.expectOne(baseUrl).flush([]);

    service().remove('a/b').subscribe();
    const removal = http.expectOne(`${baseUrl}/a%2Fb`);
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);
  });
});
