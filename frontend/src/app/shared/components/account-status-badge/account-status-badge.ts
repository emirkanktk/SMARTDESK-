import { Component, computed, input } from '@angular/core';
import { accountStatusLabel } from '../../../core/i18n/labels';
import { AccountStatus } from '../../../core/models/account.model';

/** Colored pill for an account's status (ADR-0020). */
@Component({
  selector: 'app-account-status-badge',
  template: `<span class="status" [class]="status().toLowerCase()">{{ label() }}</span>`,
  styles: `
    .status {
      display: inline-block;
      padding: 0 var(--space-2);
      border-radius: 999px;
      font-size: 0.75rem;
      font-weight: 600;
      white-space: nowrap;
    }
    .active {
      background: var(--color-success-soft);
      color: var(--color-success);
    }
    .pendingapproval {
      background: var(--color-warning-soft);
      color: var(--color-warning);
    }
    .disabled {
      background: var(--color-bg);
      color: var(--color-text-muted);
    }
  `,
})
export class AccountStatusBadge {
  readonly status = input.required<AccountStatus>();
  protected readonly label = computed(() => accountStatusLabel(this.status()));
}
