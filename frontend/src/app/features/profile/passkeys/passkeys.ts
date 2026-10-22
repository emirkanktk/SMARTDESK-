import { DOCUMENT, DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { Passkey } from '../../../core/models/passkey.model';
import { NotificationService } from '../../../core/services/notification.service';
import { PasskeyService } from '../../../core/services/passkey.service';
import { deviceName } from '../../../core/utils/webauthn';
import { Icon } from '../../../shared/components/icon/icon';
import { PasswordField } from '../../../shared/components/password-field/password-field';
import { applyServerErrors } from '../../../shared/utils/server-errors';

/** Mirrors the API's PasskeyRules.MaxNameLength. */
const MAX_NAME_LENGTH = 50;

/**
 * The user's passkeys (ADR-0057), on their profile: Face ID, a fingerprint or Windows Hello to sign
 * in with instead of the password. Lists them, adds one on this device - with the password, since
 * a passkey is a way into the account - and removes them.
 */
@Component({
  selector: 'app-passkeys',
  imports: [DatePipe, Icon, PasswordField, ReactiveFormsModule],
  styleUrl: './passkeys.scss',
  templateUrl: './passkeys.html',
})
export class Passkeys implements OnInit {
  private readonly service = inject(PasskeyService);
  private readonly notifications = inject(NotificationService);
  private readonly defaultName =
    deviceName(inject(DOCUMENT).defaultView!.navigator) ??
    $localize`:@@passkeys.defaultName:My device`;

  protected readonly supported = this.service.supported;
  protected readonly maxNameLength = MAX_NAME_LENGTH;
  protected readonly passkeys = signal<Passkey[] | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.defaultName, [Validators.required, Validators.maxLength(MAX_NAME_LENGTH), noBlank]],
    password: ['', Validators.required],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.service.list().subscribe({
      next: (passkeys) => this.passkeys.set(passkeys),
      error: () => this.loadFailed.set(true),
    });
  }

  /** The password first, then the device's own prompt: Face ID, a fingerprint, a PIN. */
  protected async add(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, password } = this.form.getRawValue();
    this.busy.set(true);
    this.formError.set(null);
    try {
      const passkey = await this.service.add(password, name.trim());
      this.passkeys.update((passkeys) => [passkey, ...(passkeys ?? [])]);
      this.form.reset({ name: this.defaultName, password: '' });
      this.notifications.success(
        $localize`:@@passkeys.added:Passkey added. Next time, sign in with it instead of your password.`,
      );
    } catch (error) {
      this.formError.set(this.addErrorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected remove(passkey: Passkey): void {
    if (
      !confirm(
        $localize`:@@passkeys.removeConfirm:Remove the passkey "${passkey.name}:name:"? It won't sign you in any more; delete it from the device too.`,
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.service
      .remove(passkey.id)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.passkeys.update((passkeys) => passkeys?.filter((p) => p.id !== passkey.id) ?? null);
          this.notifications.success($localize`:@@passkeys.removed:Passkey removed.`);
        },
        error: (error: unknown) =>
          this.notifications.error(
            error instanceof ApiError && error.status === 0
              ? error.title
              : $localize`:@@passkeys.removeFailed:The passkey could not be removed. Please try again.`,
          ),
      });
  }

  protected removeLabel(passkey: Passkey): string {
    return $localize`:@@passkeys.removeLabel:Remove ${passkey.name}:name:`;
  }

  protected errorFor(name: 'name' | 'password'): string | null {
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

  /** What the alert above the form says; a wrong password or name shows at its field instead. */
  private addErrorMessage(error: unknown): string | null {
    if (error instanceof DOMException) {
      switch (error.name) {
        // Closed, or timed out.
        case 'NotAllowedError':
        case 'AbortError':
          return $localize`:@@passkeys.notMade:No passkey was made. Try again when you're ready.`;
        // The device has one for this account already: the server named those it knows.
        case 'InvalidStateError':
          return $localize`:@@passkeys.alreadyHere:This device already has a passkey for your account.`;
        default:
          return $localize`:@@passkeys.unavailable:Passkeys can't be made on this page.`;
      }
    }
    if (!(error instanceof ApiError)) {
      return ADD_FAILED();
    }
    switch (error.status) {
      case 0:
        return error.title;
      case 400:
        return applyServerErrors(this.form, error).join(' ') || null;
      // The most passkeys an account keeps, or an address SmartDesk isn't served at.
      case 403:
      case 409:
        return error.detail ?? ADD_FAILED();
      case 429:
        return $localize`:@@error.tooManyAttempts:Too many attempts. Please wait a minute and try again.`;
      default:
        return ADD_FAILED();
    }
  }
}

const ADD_FAILED = () =>
  $localize`:@@passkeys.addFailed:The passkey could not be added. Please try again.`;

// Spaces only would be trimmed to nothing, which the API refuses.
function noBlank(control: { value: string }): { blank: true } | null {
  return control.value && !control.value.trim() ? { blank: true } : null;
}
