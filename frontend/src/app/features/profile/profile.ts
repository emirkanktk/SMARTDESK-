import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ALL_ROLES, Role } from '../../core/constants/roles';
import { roleLabel } from '../../core/i18n/labels';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { applyServerErrors } from '../../shared/utils/server-errors';

/** Mirrors the API's name rules (the same as signing up). */
const MAX_NAME_LENGTH = 100;

/**
 * The signed-in user's own account (`/profile`): their name, which they can change, and their
 * email and role, which an administrator changes.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-profile',
  styleUrl: './profile.scss',
  templateUrl: './profile.html',
})
export class Profile {
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly maxNameLength = MAX_NAME_LENGTH;
  protected readonly user = this.authService.currentUser;
  protected readonly roles = computed(() =>
    (this.user()?.roles ?? [])
      .filter((role): role is Role => ALL_ROLES.includes(role as Role))
      .map((role) => roleLabel(role))
      .join(', '),
  );

  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: [
      this.user()?.firstName ?? '',
      [Validators.required, Validators.maxLength(MAX_NAME_LENGTH), noBlank],
    ],
    lastName: [
      this.user()?.lastName ?? '',
      [Validators.required, Validators.maxLength(MAX_NAME_LENGTH), noBlank],
    ],
  });

  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set(null);
    this.authService
      .updateProfile({ firstName: value.firstName.trim(), lastName: value.lastName.trim() })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (user) => {
          this.form.setValue({ firstName: user.firstName, lastName: user.lastName });
          this.form.markAsPristine();
          this.notifications.success($localize`:@@profile.saved:Your name was saved.`);
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 400) {
            const unmatched = applyServerErrors(this.form, error);
            this.formError.set(unmatched.join(' ') || null);
            return;
          }
          this.formError.set(
            error instanceof ApiError && error.status === 0
              ? error.title
              : $localize`:@@profile.saveFailed:Your name could not be saved. Please try again.`,
          );
        },
      });
  }

  protected errorFor(name: 'firstName' | 'lastName'): string | null {
    const control = this.form.controls[name];
    if (!control.touched || !control.errors) {
      return null;
    }
    const e = control.errors;
    if (e['server']) return e['server'] as string;
    if (e['required'] || e['blank']) return $localize`:@@field.required:This field is required.`;
    if (e['maxlength']) {
      return $localize`:@@field.maxLength:Use at most ${e['maxlength'].requiredLength}:max: characters.`;
    }
    return $localize`:@@field.invalid:This value is not valid.`;
  }
}

// Spaces only would be trimmed to nothing, which the API refuses.
function noBlank(control: { value: string }): { blank: true } | null {
  return control.value && !control.value.trim() ? { blank: true } : null;
}
