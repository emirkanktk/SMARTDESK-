import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import {
  PASSWORD_HINT,
  matchingPasswordsValidator,
  passwordValidator,
} from '../../shared/validators/password.validator';

/**
 * Where the emailed reset link lands: `/reset-password?email=…&token=…`. Sets the new password,
 * then sends the user to sign in with it. The link may be opened on a device where someone is
 * still signed in; that session ends, so the sign-in page can confirm the change.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-reset-password',
  // The page's main landmark: it renders outside the shell, which provides one elsewhere.
  host: { role: 'main' },
  styleUrl: './password-pages.scss',
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  /** From the link's query string. */
  readonly email = input<string>();
  readonly token = input<string>();

  protected readonly hint = PASSWORD_HINT;
  protected readonly linkComplete = computed(() => !!this.email() && !!this.token());

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      newPassword: ['', [Validators.required, passwordValidator()]],
      confirm: ['', Validators.required],
    },
    { validators: matchingPasswordsValidator('newPassword', 'confirm') },
  );
  protected readonly saving = signal(false);
  /** The API said the link is wrong, used or expired. */
  protected readonly linkInvalid = signal(false);
  /** Identity's reasons for refusing the password, shown under the field. */
  protected readonly serverPasswordErrors = signal<readonly string[]>([]);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    // The API's reasons were about the password it saw; a different one gets checked afresh.
    this.form.controls.newPassword.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.serverPasswordErrors.set([]));
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.errorMessage.set(null);
    this.serverPasswordErrors.set([]);
    this.authService
      .resetPassword({
        email: this.email()!,
        token: this.token()!,
        newPassword: this.form.controls.newPassword.value,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          // Otherwise guestGuard would send a signed-in device past the confirmation.
          this.authService.logout();
          void this.router.navigate(['/login'], { queryParams: { reset: 'done' } });
        },
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected passwordErrors(): readonly string[] {
    const control = this.form.controls.newPassword;
    if (!control.touched) return [];
    if (control.hasError('required')) return ['Choose a new password.'];
    const missing = control.getError('password') as string[] | null;
    return missing ? [`Add ${missing.join(', ')}.`] : this.serverPasswordErrors();
  }

  protected confirmError(): string | null {
    const control = this.form.controls.confirm;
    if (!control.touched) return null;
    if (control.hasError('required')) return 'Type the new password again.';
    return this.form.hasError('passwordsDiffer') ? 'The two passwords are different.' : null;
  }

  private handleError(error: unknown): void {
    if (!(error instanceof ApiError)) {
      this.errorMessage.set('Something went wrong. Please try again.');
      return;
    }
    if (error.status === 400) {
      const fields = Object.fromEntries(
        Object.entries(error.fieldErrors).map(([key, messages]) => [key.toLowerCase(), messages]),
      );
      if (fields['token']) {
        this.linkInvalid.set(true);
      } else if (fields['newpassword']) {
        this.serverPasswordErrors.set(fields['newpassword']);
      } else {
        this.errorMessage.set(error.detail ?? error.title);
      }
      return;
    }
    this.errorMessage.set(
      error.status === 429
        ? 'Too many attempts. Please wait a minute and try again.'
        : error.status === 0
          ? error.title
          : 'Something went wrong. Please try again.',
    );
  }
}
