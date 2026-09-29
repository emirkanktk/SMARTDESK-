import { Component, effect, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { missingRulesMessage, passwordValidator } from '../../validators/password.validator';
import { generateTemporaryPassword } from '../../utils/temporary-password';

let nextId = 0;

/**
 * Sets a temporary password for someone else's account (ADR-0021): typed, or generated, and
 * shown in plain text so it can be handed over. The owner replaces it at their next sign-in.
 * The parent calls the API; this only collects a password that meets the policy.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-temporary-password-form',
  templateUrl: './temporary-password-form.html',
  styleUrl: './temporary-password-form.scss',
})
export class TemporaryPasswordForm {
  /** Whose password it is, for the label. */
  readonly name = input.required<string>();
  readonly busy = input(false);
  /** The API's reason for refusing the last one, if any. */
  readonly serverError = input<string | null>(null);

  readonly saved = output<string>();
  readonly cancelled = output<void>();

  protected readonly inputId = `temporary-password-${nextId++}`;
  protected readonly password = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, passwordValidator()],
  });
  protected readonly form = new FormGroup({ password: this.password });

  constructor() {
    // A refused password shows its reason until it's edited.
    effect(() => {
      const error = this.serverError();
      if (error) {
        this.password.setErrors({ server: error });
        this.password.markAsTouched();
      }
    });
  }

  protected generate(): void {
    this.password.setValue(generateTemporaryPassword());
    this.password.markAsTouched();
  }

  protected submit(): void {
    if (this.password.invalid) {
      this.password.markAsTouched();
      return;
    }
    this.saved.emit(this.password.value);
  }

  protected error(): string | null {
    if (!this.password.touched || this.password.valid) return null;
    if (this.password.hasError('server')) return this.password.getError('server') as string;
    if (this.password.hasError('required')) {
      return $localize`:@@accounts.passwordRequired:Type or generate a password.`;
    }
    const missing = this.password.getError('password') as string[];
    return missingRulesMessage(missing);
  }
}
