import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { applyServerErrors } from '../../shared/utils/server-errors';
import {
  PASSWORD_HINT,
  matchingPasswordsValidator,
  passwordValidator,
  PASSWORD_MESSAGES,
  missingRulesMessage,
} from '../../shared/validators/password.validator';

/** The API's limit on names (AccountRules.MaxNameLength). */
const MAX_NAME_LENGTH = 100;

export type AccountType = 'member' | 'manager';

/**
 * Public sign-up (ADR-0020, ADR-0021). A team member gives their manager's email and waits for
 * that manager's approval; a manager waits for an administrator's. Either way the screen ends on
 * "check your email" rather than signing anyone in, and it says the same thing whether or not
 * the email already had an account or the manager's email matched anyone.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-register',
  host: { role: 'main' },
  styleUrl: './register.scss',
  templateUrl: './register.html',
})
export class Register {
  private readonly authService = inject(AuthService);

  protected readonly hint = PASSWORD_HINT;
  protected readonly maxNameLength = MAX_NAME_LENGTH;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      accountType: ['member' as AccountType],
      firstName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
      lastName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
      email: ['', [Validators.required, Validators.email]],
      managerEmail: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, passwordValidator()]],
      confirm: ['', Validators.required],
    },
    { validators: matchingPasswordsValidator('password', 'confirm') },
  );

  protected readonly accountType = toSignal(this.form.controls.accountType.valueChanges, {
    initialValue: this.form.controls.accountType.value,
  });
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Where the confirmation went, and which kind of sign-up it was, once it's sent. */
  protected readonly sent = signal<{ email: string; type: AccountType } | null>(null);

  constructor() {
    // A manager's email is only asked of someone joining a team.
    this.form.controls.accountType.valueChanges.pipe(takeUntilDestroyed()).subscribe((type) => {
      const managerEmail = this.form.controls.managerEmail;
      if (type === 'member') {
        managerEmail.enable();
      } else {
        managerEmail.disable();
      }
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { accountType, firstName, lastName, email, managerEmail, password } =
      this.form.getRawValue();
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.authService
      .register({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        password,
        managerEmail: accountType === 'member' ? managerEmail.trim() : null,
      })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => this.sent.set({ email: email.trim(), type: accountType }),
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected fieldError(
    control: 'firstName' | 'lastName' | 'email' | 'managerEmail',
  ): string | null {
    const c = this.form.controls[control];
    if (!c.touched || c.valid || c.disabled) return null;
    if (c.hasError('server')) return c.getError('server') as string;
    if (c.hasError('required')) {
      return control === 'email'
        ? $localize`:@@field.emailRequired:Email is required.`
        : control === 'managerEmail'
          ? $localize`:@@register.managerEmailRequired:Enter your manager's work email.`
          : $localize`:@@field.required:This field is required.`;
    }
    if (c.hasError('maxlength')) {
      return $localize`:@@field.maxLength:Use at most ${MAX_NAME_LENGTH}:max: characters.`;
    }
    return $localize`:@@field.emailInvalid:Enter a valid email address.`;
  }

  protected passwordErrors(): readonly string[] {
    const control = this.form.controls.password;
    if (!control.touched) return [];
    if (control.hasError('required')) return [PASSWORD_MESSAGES.choose()];
    const missing = control.getError('password') as string[] | null;
    if (missing) return [missingRulesMessage(missing)];
    const server = control.getError('server') as string | null;
    return server ? [server] : [];
  }

  protected confirmError(): string | null {
    const control = this.form.controls.confirm;
    if (!control.touched) return null;
    if (control.hasError('required')) return PASSWORD_MESSAGES.repeat();
    return this.form.hasError('passwordsDiffer') ? PASSWORD_MESSAGES.mismatch() : null;
  }

  private handleError(error: unknown): void {
    if (!(error instanceof ApiError)) {
      this.errorMessage.set($localize`:@@error.generic:Something went wrong. Please try again.`);
      return;
    }
    if (error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.errorMessage.set(unmatched[0] ?? null);
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
