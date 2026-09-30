import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { Backdrop } from '../../shared/components/backdrop/backdrop';
import { NotificationService } from '../../core/services/notification.service';
import {
  PASSWORD_HINT,
  matchingPasswordsValidator,
  passwordValidator,
  PASSWORD_MESSAGES,
  missingRulesMessage,
} from '../../shared/validators/password.validator';

/**
 * Replaces the signed-in user's password (ADR-0021). Reached two ways: by choice, from the top
 * bar, or forced - after signing in with a temporary password someone else set, when the API
 * answers nothing else until it's replaced. Every other session ends; this one gets a new token.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, Backdrop],
  selector: 'app-change-password',
  host: { role: 'main' },
  styleUrl: './password-pages.scss',
  templateUrl: './change-password.html',
})
export class ChangePassword {
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  protected readonly hint = PASSWORD_HINT;
  /** Forced: a temporary password must be replaced before anything else. */
  protected readonly forced = this.authService.passwordChangeRequired;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, passwordValidator()]],
      confirm: ['', Validators.required],
    },
    { validators: matchingPasswordsValidator('newPassword', 'confirm') },
  );
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** The API's reasons, shown under their field until that field changes. */
  protected readonly serverErrors = signal<{ current?: string; new?: readonly string[] }>({});

  constructor() {
    this.form.controls.currentPassword.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.serverErrors.update(({ new: n }) => ({ new: n })));
    this.form.controls.newPassword.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.serverErrors.update(({ current }) => ({ current })));
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { currentPassword, newPassword } = this.form.getRawValue();
    this.saving.set(true);
    this.errorMessage.set(null);
    this.authService
      .changePassword({ currentPassword, newPassword })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success(
            $localize`:@@change.done:Your password has been changed. Other devices were signed out.`,
          );
          void this.router.navigateByUrl('/dashboard');
        },
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected signOut(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/');
  }

  protected currentError(): string | null {
    const control = this.form.controls.currentPassword;
    if (this.serverErrors().current) return this.serverErrors().current!;
    return control.touched && control.hasError('required')
      ? $localize`:@@change.currentRequired:Enter your current password.`
      : null;
  }

  protected newPasswordErrors(): readonly string[] {
    const control = this.form.controls.newPassword;
    if (!control.touched) return this.serverErrors().new ?? [];
    if (control.hasError('required')) return [PASSWORD_MESSAGES.chooseNew()];
    const missing = control.getError('password') as string[] | null;
    return missing ? [missingRulesMessage(missing)] : (this.serverErrors().new ?? []);
  }

  protected confirmError(): string | null {
    const control = this.form.controls.confirm;
    if (!control.touched) return null;
    if (control.hasError('required')) return PASSWORD_MESSAGES.repeatNew();
    return this.form.hasError('passwordsDiffer') ? PASSWORD_MESSAGES.mismatch() : null;
  }

  private handleError(error: unknown): void {
    if (!(error instanceof ApiError)) {
      this.errorMessage.set($localize`:@@error.generic:Something went wrong. Please try again.`);
      return;
    }
    if (error.status === 400) {
      const fields = Object.fromEntries(
        Object.entries(error.fieldErrors).map(([key, messages]) => [key.toLowerCase(), messages]),
      );
      const current = fields['currentpassword']?.[0];
      const next = fields['newpassword'];
      if (current || next) {
        this.serverErrors.set({ current, new: next });
      } else {
        this.errorMessage.set(error.detail ?? error.title);
      }
      return;
    }
    this.errorMessage.set(
      error.status === 429
        ? $localize`:@@error.tooManyAttempts:Too many attempts. Please wait a minute and try again.`
        : error.status === 0
          ? error.title
          : $localize`:@@error.generic:Something went wrong. Please try again.`,
    );
  }
}
