import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { ALL_ROLES, Role, Roles } from '../../core/constants/roles';
import { AccountStatus, UserAccount } from '../../core/models/account.model';
import { ApiError } from '../../core/models/api-error.model';
import { AccountService } from '../../core/services/account.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { AccountStatusBadge } from '../../shared/components/account-status-badge/account-status-badge';
import { TemporaryPasswordForm } from '../../shared/components/temporary-password-form/temporary-password-form';
import { accountActionError } from '../../shared/utils/account-errors';
import { applyServerErrors } from '../../shared/utils/server-errors';
import { generateTemporaryPassword } from '../../shared/utils/temporary-password';
import { missingRulesMessage, passwordValidator } from '../../shared/validators/password.validator';
import { roleLabel } from '../../core/i18n/labels';
import { teamName } from '../../shared/utils/resource-access';

/** The API's limit on names (AccountRules.MaxNameLength). */
const MAX_NAME_LENGTH = 100;

type StatusFilter = AccountStatus | '';

/**
 * Every account, for Admins (ADR-0020): sign-ups waiting for approval first, then everyone by
 * name. From here an Admin approves or rejects sign-ups, adds accounts, changes roles, moves
 * employees between teams, switches accounts off and on, and sends password-reset links. The
 * API enforces every rule; the screen only hides what can't apply (e.g. your own row).
 */
@Component({
  imports: [ReactiveFormsModule, DatePipe, AccountStatusBadge, TemporaryPasswordForm],
  selector: 'app-users',
  styleUrl: './users.scss',
  templateUrl: './users.html',
})
export class Users {
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly roles = ALL_ROLES;
  protected readonly maxNameLength = MAX_NAME_LENGTH;

  private readonly accounts = signal<UserAccount[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the account an action is running on, to disable its controls meanwhile. */
  protected readonly busyId = signal<string | null>(null);

  protected readonly statusFilter = signal<StatusFilter>('');
  protected readonly roleFilter = signal<Role | ''>('');
  protected readonly search = signal('');

  protected readonly currentUserId = computed(() => this.authService.currentUser()?.id ?? null);
  protected readonly pendingCount = computed(
    () => this.accounts().filter((a) => a.status === 'PendingApproval').length,
  );
  /** Active Managers, for the team pickers. */
  protected readonly managers = computed(() =>
    this.accounts().filter((a) => a.role === Roles.Manager && a.status === 'Active'),
  );

  protected readonly rows = computed(() => {
    const status = this.statusFilter();
    const role = this.roleFilter();
    const term = this.search().trim().toLowerCase();
    return this.accounts()
      .filter(
        (a) =>
          (!status || a.status === status) &&
          (!role || a.role === role) &&
          (!term || `${a.displayName} ${a.email}`.toLowerCase().includes(term)),
      )
      .sort(compareAccounts);
  });

  protected readonly total = computed(() => this.accounts().length);

  protected readonly showForm = signal(false);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
    lastName: ['', [Validators.required, Validators.maxLength(MAX_NAME_LENGTH)]],
    email: ['', [Validators.required, Validators.email]],
    role: [Roles.Employee as Role, Validators.required],
    managerId: [''],
    password: ['', [Validators.required, passwordValidator()]],
  });

  /** The account whose temporary password is being set, and the API's reason if refused. */
  protected readonly passwordFor = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.accountService.getAccounts().subscribe({
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

  protected setStatusFilter(status: StatusFilter): void {
    this.statusFilter.set(status);
  }

  protected setRoleFilter(event: Event): void {
    this.roleFilter.set((event.target as HTMLSelectElement).value as Role | '');
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected readonly roleLabel = roleLabel;

  protected roleOfLabel(account: UserAccount): string {
    return $localize`:@@users.roleOf:Role of ${account.displayName}:name:`;
  }

  protected teamOfLabel(account: UserAccount): string {
    return $localize`:@@users.teamOf:Team of ${account.displayName}:name:`;
  }

  protected isSelf(account: UserAccount): boolean {
    return account.id === this.currentUserId();
  }

  /** False when an employee's manager has since been disabled or given another role. */
  protected isActiveManager(id: string): boolean {
    return this.managers().some((m) => m.id === id);
  }

  protected approve(account: UserAccount): void {
    this.run(account, this.accountService.approve(account.id), (updated) => {
      this.replace(updated);
      this.notifications.success(
        $localize`:@@accounts.approved:${updated.displayName}:name: can sign in now. They've been emailed.`,
      );
    });
  }

  protected reject(account: UserAccount): void {
    if (
      !confirm(
        $localize`:@@accounts.confirmReject:Reject the sign-up of ${account.displayName}:name:? It will be deleted.`,
      )
    ) {
      return;
    }
    this.run(account, this.accountService.reject(account.id), () => {
      this.accounts.update((list) => list.filter((a) => a.id !== account.id));
      this.notifications.success(
        $localize`:@@accounts.rejected:The sign-up of ${account.displayName}:name: was rejected.`,
      );
    });
  }

  protected changeRole(account: UserAccount, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const role = select.value as Role;
    if (role === account.role) {
      return;
    }
    if (!confirm(makeRoleQuestion(account.displayName, role))) {
      select.value = account.role;
      return;
    }
    this.run(
      account,
      this.accountService.changeRole(account.id, role),
      (updated) => {
        this.replace(updated);
        this.notifications.success(nowRoleMessage(updated.displayName, role));
        // Their team (if they had one as an Employee) changed too.
        if (account.managerId) this.refreshTeamSizes();
      },
      () => (select.value = account.role),
    );
  }

  protected assignManager(account: UserAccount, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const managerId = select.value || null;
    this.run(
      account,
      this.accountService.assignManager(account.id, managerId),
      (updated) => {
        this.replace(updated);
        this.refreshTeamSizes();
        this.notifications.success(
          updated.managerName
            ? $localize`:@@users.nowInTeam:${updated.displayName}:name: is now in ${teamName(updated.managerName)}:team:.`
            : $localize`:@@users.noLongerInTeam:${updated.displayName}:name: is no longer in a team.`,
        );
      },
      () => (select.value = account.managerId ?? ''),
    );
  }

  protected setEnabled(account: UserAccount, enabled: boolean): void {
    if (
      !enabled &&
      !confirm(
        $localize`:@@accounts.confirmDisable:Disable ${account.displayName}:name:? They'll be signed out and can't sign in.`,
      )
    ) {
      return;
    }
    this.run(account, this.accountService.setEnabled(account.id, enabled), (updated) => {
      this.replace(updated);
      this.notifications.success(
        enabled
          ? $localize`:@@accounts.enabled:${updated.displayName}:name: is enabled again.`
          : $localize`:@@accounts.disabled:${updated.displayName}:name: is disabled.`,
      );
    });
  }

  protected openPassword(account: UserAccount): void {
    this.passwordFor.set(account.id);
    this.passwordError.set(null);
  }

  protected closePassword(): void {
    this.passwordFor.set(null);
    this.passwordError.set(null);
  }

  protected setTemporaryPassword(account: UserAccount, password: string): void {
    this.passwordError.set(null);
    this.busyId.set(account.id);
    this.accountService
      .setTemporaryPassword(account.id, password)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.replace(updated);
          this.closePassword();
          this.notifications.success(
            $localize`:@@accounts.tempPasswordSet:${updated.displayName}:name: has a temporary password now and has been signed out.`,
          );
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 400) {
            this.passwordError.set(Object.values(error.fieldErrors).flat()[0] ?? error.title);
            return;
          }
          const message = accountActionError(error);
          if (message) this.notifications.error(message);
        },
      });
  }

  protected generatePassword(): void {
    this.form.controls.password.setValue(generateTemporaryPassword());
    this.form.controls.password.markAsTouched();
  }

  protected toggleForm(): void {
    this.showForm.update((shown) => !shown);
    this.formError.set(null);
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set(null);
    this.accountService
      .createAccount({
        firstName: value.firstName.trim(),
        lastName: value.lastName.trim(),
        email: value.email.trim(),
        role: value.role,
        password: value.password,
        managerId: value.role === Roles.Employee ? value.managerId || null : null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (account) => {
          this.accounts.update((list) => [...list, account]);
          if (account.managerId) this.refreshTeamSizes();
          this.notifications.success(
            $localize`:@@accounts.added:${account.displayName}:name: was added. Give them the temporary password; they'll choose their own when they first sign in.`,
          );
          this.form.reset({
            firstName: '',
            lastName: '',
            email: '',
            role: Roles.Employee,
            managerId: '',
            password: '',
          });
          this.showForm.set(false);
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

  protected fieldError(control: 'firstName' | 'lastName' | 'email' | 'password'): string | null {
    const c = this.form.controls[control];
    if (!c.touched || c.valid) return null;
    if (c.hasError('server')) return c.getError('server') as string;
    if (c.hasError('required')) {
      return control === 'password'
        ? $localize`:@@accounts.passwordRequired:Type or generate a password.`
        : $localize`:@@field.required:This field is required.`;
    }
    if (c.hasError('maxlength')) {
      return $localize`:@@field.maxLength:Use at most ${MAX_NAME_LENGTH}:max: characters.`;
    }
    const missing = c.getError('password') as string[] | null;
    return missing
      ? missingRulesMessage(missing)
      : $localize`:@@field.emailInvalid:Enter a valid email address.`;
  }

  private run<T>(
    account: UserAccount,
    action: Observable<T>,
    done: (result: T) => void,
    failed?: () => void,
  ): void {
    this.busyId.set(account.id);
    action.pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: done,
      error: (error: unknown) => {
        failed?.();
        const message = accountActionError(error);
        if (message) this.notifications.error(message);
        if (error instanceof ApiError && error.status === 404) this.load();
      },
    });
  }

  private replace(updated: UserAccount): void {
    this.accounts.update((list) => list.map((a) => (a.id === updated.id ? updated : a)));
  }

  /** Team sizes are counted by the API; re-read the list quietly after a team changes. */
  private refreshTeamSizes(): void {
    this.accountService.getAccounts().subscribe({
      next: (accounts) => this.accounts.set(accounts),
      error: () => undefined,
    });
  }
}

function compareAccounts(a: UserAccount, b: UserAccount): number {
  const pending = Number(b.status === 'PendingApproval') - Number(a.status === 'PendingApproval');
  return pending || a.displayName.localeCompare(b.displayName) || a.email.localeCompare(b.email);
}

function makeRoleQuestion(name: string, role: Role): string {
  switch (role) {
    case Roles.Admin:
      return $localize`:@@users.makeAdmin:Make ${name}:name: an Admin?`;
    case Roles.Manager:
      return $localize`:@@users.makeManager:Make ${name}:name: a Manager?`;
    case Roles.Employee:
      return $localize`:@@users.makeEmployee:Make ${name}:name: an Employee?`;
  }
}

function nowRoleMessage(name: string, role: Role): string {
  switch (role) {
    case Roles.Admin:
      return $localize`:@@users.nowAdmin:${name}:name: is now an Admin.`;
    case Roles.Manager:
      return $localize`:@@users.nowManager:${name}:name: is now a Manager.`;
    case Roles.Employee:
      return $localize`:@@users.nowEmployee:${name}:name: is now an Employee.`;
  }
}
