import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { PasskeyService } from '../../core/services/passkey.service';
import { AuthShowcase } from '../../shared/components/auth-showcase/auth-showcase';
import { Backdrop } from '../../shared/components/backdrop/backdrop';
import { Icon } from '../../shared/components/icon/icon';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
import { Logo } from '../../shared/components/logo/logo';
import { PasswordField } from '../../shared/components/password-field/password-field';
import { ThemeSwitch } from '../../shared/components/theme-switch/theme-switch';

const DEFAULT_REDIRECT = '/dashboard';

@Component({
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AuthShowcase,
    Backdrop,
    Icon,
    LanguageSwitch,
    Logo,
    PasswordField,
    ThemeSwitch,
  ],
  selector: 'app-login',
  // The page's main landmark: it renders outside the shell, which provides one elsewhere.
  host: { role: 'main' },
  styleUrl: './login.scss',
  templateUrl: './login.html',
})
export class Login {
  private readonly authService = inject(AuthService);
  private readonly passkeys = inject(PasskeyService);
  private readonly router = inject(Router);

  /** Where to go after signing in; bound from the `returnUrl` query parameter. */
  readonly returnUrl = input<string | undefined>();
  /** "done" after a password reset, to confirm it worked. */
  readonly reset = input<string | undefined>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  /** Signing in with a passkey (ADR-0057), where this browser can. */
  protected readonly passkeysSupported = this.passkeys.supported;
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    this.authService
      .login(this.form.getRawValue())
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => void this.router.navigateByUrl(safeRedirect(this.returnUrl())),
        error: (error: unknown) => this.errorMessage.set(loginErrorMessage(error)),
      });
  }

  /** The device offers the passkeys it has for SmartDesk; no email needed. */
  protected async signInWithPasskey(): Promise<void> {
    this.submitting.set(true);
    this.errorMessage.set(null);
    try {
      await this.passkeys.signIn();
      void this.router.navigateByUrl(safeRedirect(this.returnUrl()));
    } catch (error) {
      this.errorMessage.set(passkeyErrorMessage(error));
    } finally {
      this.submitting.set(false);
    }
  }

  protected showError(control: 'email' | 'password'): boolean {
    const c = this.form.controls[control];
    return c.invalid && c.touched;
  }
}

const SIGN_IN_FAILED = () => $localize`:@@login.failed:Sign-in failed. Please try again.`;
const TOO_MANY_ATTEMPTS = () =>
  $localize`:@@error.tooManyAttempts:Too many attempts. Please wait a minute and try again.`;

function loginErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return SIGN_IN_FAILED();
  }

  switch (error.status) {
    case 0:
      return error.title;
    case 400:
      return (
        Object.values(error.fieldErrors).flat()[0] ??
        $localize`:@@login.checkFields:Please check the email and password.`
      );
    case 401:
      return $localize`:@@login.invalidCredentials:Invalid email or password.`;
    case 403:
      // The right password, but the account is waiting for approval or was switched off; the
      // API says which, and what to do.
      return error.detail ?? $localize`:@@login.cannotSignIn:This account can't sign in right now.`;
    case 429:
      return TOO_MANY_ATTEMPTS();
    default:
      return SIGN_IN_FAILED();
  }
}

function passkeyErrorMessage(error: unknown): string {
  if (error instanceof DOMException) {
    // Closed, timed out, or no passkey for SmartDesk on this device.
    return error.name === 'NotAllowedError' || error.name === 'AbortError'
      ? $localize`:@@login.passkeyNotUsed:No passkey was used. Try again, or sign in with your password.`
      : $localize`:@@login.passkeyUnavailable:Passkeys can't be used on this page. Sign in with your password.`;
  }
  if (!(error instanceof ApiError)) {
    return SIGN_IN_FAILED();
  }

  switch (error.status) {
    case 0:
      return error.title;
    // The API says what went wrong: the passkey isn't on an account any more, the request took
    // too long, the account is switched off, or this isn't SmartDesk's address.
    case 401:
    case 403:
      return error.detail ?? SIGN_IN_FAILED();
    case 429:
      return TOO_MANY_ATTEMPTS();
    default:
      return SIGN_IN_FAILED();
  }
}

// Only follow in-app paths, so a crafted link can't bounce the user to another site after login.
function safeRedirect(url: string | undefined): string {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url.startsWith('/login')) {
    return DEFAULT_REDIRECT;
  }
  return url;
}
