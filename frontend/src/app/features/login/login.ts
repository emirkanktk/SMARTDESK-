import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';

const DEFAULT_REDIRECT = '/dashboard';

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-login',
  styleUrl: './login.scss',
  templateUrl: './login.html',
})
export class Login {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  /** Where to go after signing in; bound from the `returnUrl` query parameter. */
  readonly returnUrl = input<string | undefined>();
  /** "done" after a password reset, to confirm it worked. */
  readonly reset = input<string | undefined>();

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

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

  protected showError(control: 'email' | 'password'): boolean {
    const c = this.form.controls[control];
    return c.invalid && c.touched;
  }
}

function loginErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Sign-in failed. Please try again.';
  }

  switch (error.status) {
    case 0:
      return error.title;
    case 400:
      return Object.values(error.fieldErrors).flat()[0] ?? 'Please check the email and password.';
    case 401:
      return 'Invalid email or password.';
    case 403:
      // The right password, but the account is waiting for approval or was switched off; the
      // API says which, and what to do.
      return error.detail ?? "This account can't sign in right now.";
    case 429:
      return 'Too many attempts. Please wait a minute and try again.';
    default:
      return 'Sign-in failed. Please try again.';
  }
}

// Only follow in-app paths, so a crafted link can't bounce the user to another site after login.
function safeRedirect(url: string | undefined): string {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url.startsWith('/login')) {
    return DEFAULT_REDIRECT;
  }
  return url;
}
