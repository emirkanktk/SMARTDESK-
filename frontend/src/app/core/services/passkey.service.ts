import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { LoginResponse } from '../models/auth.model';
import { Passkey, PasskeyOptions } from '../models/passkey.model';
import {
  creationOptions,
  credentialJson,
  passkeysSupported,
  requestOptions,
} from '../utils/webauthn';
import { AuthService } from './auth.service';

/**
 * Passkeys (ADR-0057): Face ID, a fingerprint or Windows Hello instead of a password. Each time the
 * server hands out a challenge with a token, the device's authenticator answers it, and the answer
 * goes back with the token. Making one takes the account's password first. Where the person closes
 * the device's prompt, the browser's own error comes through: a DOMException named NotAllowedError.
 */
@Injectable({ providedIn: 'root' })
export class PasskeyService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly baseUrl = `${environment.apiUrl}/auth/passkeys`;

  /** Whether this page can make and use passkeys: a browser with WebAuthn, on https or localhost. */
  readonly supported = passkeysSupported(this.window);

  list(): Observable<Passkey[]> {
    return this.http.get<Passkey[]>(this.baseUrl, { context: quietly() });
  }

  /** Makes a passkey on this device, with the account's password, and keeps it under this name. */
  async add(password: string, name: string): Promise<Passkey> {
    const start = await firstValueFrom(
      this.http.post<PasskeyOptions<PublicKeyCredentialCreationOptionsJSON>>(
        `${this.baseUrl}/creation-options`,
        { password },
        { context: quietly() },
      ),
    );
    const credential = await this.credentials().create({
      publicKey: creationOptions(start.options),
    });
    return firstValueFrom(
      this.http.post<Passkey>(
        this.baseUrl,
        { credential: credentialJson(answered(credential)), token: start.token, name },
        { context: quietly() },
      ),
    );
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`, {
      context: quietly(),
    });
  }

  /** Signs in with a passkey this device has for SmartDesk: the person picks theirs. */
  async signIn(): Promise<LoginResponse> {
    const start = await firstValueFrom(
      this.http.post<PasskeyOptions<PublicKeyCredentialRequestOptionsJSON>>(
        `${this.baseUrl}/request-options`,
        {},
        {
          context: new HttpContext()
            .set(ANONYMOUS_REQUEST, true)
            .set(SKIP_ERROR_NOTIFICATION, true),
        },
      ),
    );
    const credential = await this.credentials().get({ publicKey: requestOptions(start.options) });
    return firstValueFrom(
      this.auth.passkeySignIn(credentialJson(answered(credential)), start.token),
    );
  }

  private credentials(): CredentialsContainer {
    if (!this.supported || !this.window) {
      throw new DOMException('Passkeys are not available here.', 'NotSupportedError');
    }
    return this.window.navigator.credentials;
  }
}

/** A prompt that ended without a passkey counts as the person closing it. */
function answered(credential: Credential | null): PublicKeyCredential {
  if (!credential) {
    throw new DOMException('No passkey was used.', 'NotAllowedError');
  }
  return credential as PublicKeyCredential;
}

// The screens say what went wrong themselves.
function quietly(): HttpContext {
  return new HttpContext().set(SKIP_ERROR_NOTIFICATION, true);
}
