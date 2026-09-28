import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { UserAccount } from '../../core/models/account.model';
import { ApiError } from '../../core/models/api-error.model';
import { AccountService } from '../../core/services/account.service';
import { NotificationService } from '../../core/services/notification.service';
import { AccountStatusBadge } from '../../shared/components/account-status-badge/account-status-badge';
import { TemporaryPasswordForm } from '../../shared/components/temporary-password-form/temporary-password-form';
import { accountActionError } from '../../shared/utils/account-errors';
import { applyServerErrors } from '../../shared/utils/server-errors';
import { generateTemporaryPassword } from '../../shared/utils/temporary-password';
import { passwordValidator } from '../../shared/validators/password.validator';

/** The API's limit on names (AccountRules.MaxNameLength). */
const MAX_NAME_LENGTH = 100;

/**
 * A Manager's own team (ADR-0020, ADR-0021):
 * - Requests to join it, from people who signed up with this manager's email: approve or reject.
 * - Members: add one with a temporary password, set a new temporary password, switch them off
 *   and on, take them out of the team.
 * Accounts outside the team aren't reachable from here - the API answers 404 for them.
 */
@Component({
  imports: [ReactiveFormsModule, AccountStatusBadge, TemporaryPasswordForm],
  selector: 'app-team',
  styleUrl: './team.scss',
  templateUrl: './team.html',
})
export class Team {
  private readonly accountService = inject(AccountService);
  private readonly notifications = inject(NotificationService);

  protected readonly maxNameLength = MAX_NAME_LENGTH;

  private readonly accounts = signal<UserAccount[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the account an action is running on, to disable its buttons meanwhile. */
  protected readonly busyId = signal<string | null>(null);

  protected readonly requests = computed(() =>
    this.accounts()
      .filter((a) => a.status === 'PendingApproval')
      .sort(byName),
  );
  protected readonly rows = computed(() =>
    this.accounts()
      .filter((a) => a.status !== 'PendingApproval')
      .sort(byName),
  );

  /** The member whose temporary password is being set, and the API's reason if refused. */
  protected readonly passwordFor = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
    lastName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, passwordValidator()]],
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.accountService.getTeam().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.loading.set(false);
      },
      error: () => {
        this.loadFailed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected approve(request: UserAccount): void {
    this.run(request, this.accountService.approveRequest(request.id), (member) => {
      this.replace(member);
      this.notifications.success(`${member.displayName} joined your team. They've been emailed.`);
    });
  }

  protected reject(request: UserAccount): void {
    if (
      !confirm(`Reject ${request.displayName}'s request to join your team? It will be deleted.`)
    ) {
      return;
    }
    this.run(request, this.accountService.rejectRequest(request.id), () => {
      this.remove(request.id);
      this.notifications.success(`${request.displayName}'s request was rejected.`);
    });
  }

  protected generatePassword(): void {
    this.form.controls.password.setValue(generateTemporaryPassword());
    this.form.controls.password.markAsTouched();
  }

  protected add(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set(null);
    this.accountService
      .addTeamMember({
        firstName: value.firstName.trim(),
        lastName: value.lastName.trim(),
        email: value.email.trim(),
        password: value.password,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (member) => {
          this.accounts.update((list) => [...list, member]);
          this.notifications.success(
            `${member.displayName} was added. Give them the temporary password; they'll choose their own when they first sign in.`,
          );
          this.form.reset();
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 400) {
            this.formError.set(applyServerErrors(this.form, error)[0] ?? null);
          } else {
            this.formError.set(accountActionError(error));
          }
        },
      });
  }

  protected openPassword(member: UserAccount): void {
    this.passwordFor.set(member.id);
    this.passwordError.set(null);
  }

  protected closePassword(): void {
    this.passwordFor.set(null);
    this.passwordError.set(null);
  }

  protected setTemporaryPassword(member: UserAccount, password: string): void {
    this.passwordError.set(null);
    this.busyId.set(member.id);
    this.accountService
      .setTeamMemberTemporaryPassword(member.id, password)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.replace(updated);
          this.closePassword();
          this.notifications.success(
            `${updated.displayName} has a temporary password now and has been signed out.`,
          );
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 400) {
            this.passwordError.set(Object.values(error.fieldErrors).flat()[0] ?? error.title);
            return;
          }
          this.reportFailure(error);
        },
      });
  }

  protected setEnabled(member: UserAccount, enabled: boolean): void {
    if (
      !enabled &&
      !confirm(`Disable ${member.displayName}? They'll be signed out and can't sign in.`)
    ) {
      return;
    }
    this.run(member, this.accountService.setTeamMemberEnabled(member.id, enabled), (updated) => {
      this.replace(updated);
      this.notifications.success(
        `${updated.displayName} is ${enabled ? 'enabled again' : 'disabled'}.`,
      );
    });
  }

  protected removeFromTeam(member: UserAccount): void {
    if (
      !confirm(
        `Take ${member.displayName} out of your team? Their account stays; an administrator can move it to another team.`,
      )
    ) {
      return;
    }
    this.run(member, this.accountService.removeTeamMember(member.id), () => {
      this.remove(member.id);
      this.notifications.success(`${member.displayName} is no longer in your team.`);
    });
  }

  protected fieldError(control: 'firstName' | 'lastName' | 'email' | 'password'): string | null {
    const c = this.form.controls[control];
    if (!c.touched || c.valid) return null;
    if (c.hasError('server')) return c.getError('server') as string;
    if (c.hasError('required')) {
      return control === 'password' ? 'Type or generate a password.' : 'This field is required.';
    }
    if (c.hasError('maxlength')) return `Use at most ${MAX_NAME_LENGTH} characters.`;
    const missing = c.getError('password') as string[] | null;
    return missing ? `Add ${missing.join(', ')}.` : 'Enter a valid email address.';
  }

  private run<T>(account: UserAccount, action: Observable<T>, done: (result: T) => void): void {
    this.busyId.set(account.id);
    action.pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: done,
      error: (error: unknown) => this.reportFailure(error),
    });
  }

  private reportFailure(error: unknown): void {
    const message = accountActionError(error);
    if (message) this.notifications.error(message);
    // 404: they're no longer in the team (an admin moved them); show the team as it is.
    if (error instanceof ApiError && error.status === 404) this.load();
  }

  private replace(updated: UserAccount): void {
    this.accounts.update((list) => list.map((a) => (a.id === updated.id ? updated : a)));
  }

  private remove(id: string): void {
    this.accounts.update((list) => list.filter((a) => a.id !== id));
  }
}

function byName(a: UserAccount, b: UserAccount): number {
  return a.displayName.localeCompare(b.displayName) || a.email.localeCompare(b.email);
}
