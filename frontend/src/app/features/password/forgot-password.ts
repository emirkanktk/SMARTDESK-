import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';

/**
 * Asks for a password-reset link. Whatever the API knows, the screen says the same thing
 * afterwards - "if an account exists, a link is on its way" - so it can't be used to find out
 * which emails are registered (ADR-0019).
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-forgot-password',
  // The page's main landmark: it renders outside the shell, which provides one elsewhere.
  host: { role: 'main' },
  styleUrl: './password-pages.scss',
  templateUrl: './forgot-password.html',
})
export class ForgotPassword {
  private readonly authService = inject(AuthService);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });
  protected readonly sending = signal(false);
  /** The address the link was requested for, once the API has answered. */
  protected readonly sentTo = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const email = this.form.controls.email.value.trim();
    this.sending.set(true);
    this.errorMessage.set(null);
    this.authService
      .forgotPassword(email)
      .pipe(finalize(() => this.sending.set(false)))
      .subscribe({
        next: () => this.sentTo.set(email),
        error: (error: unknown) => this.errorMessage.set(requestError(error)),
      });
  }

  protected showError(): boolean {
    const control = this.form.controls.email;
    return control.invalid && control.touched;
  }
}

function requestError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 0) return error.title;
    if (error.status === 429)
      return $localize`:@@error.tooManyAttempts:Too many attempts. Please wait a minute and try again.`;
    if (error.status === 400) return $localize`:@@field.emailInvalid:Enter a valid email address.`;
  }
  return $localize`:@@error.generic:Something went wrong. Please try again.`;
}
