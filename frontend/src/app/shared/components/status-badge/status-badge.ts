import { Component, computed, input } from '@angular/core';
import { ResourceStatus } from '../../../core/models/resource.model';

const LABELS: Record<ResourceStatus, string> = {
  Active: 'Active',
  Inactive: 'Inactive',
  Maintenance: 'Maintenance',
};

/** Colored pill for a resource's status. */
@Component({
  selector: 'app-status-badge',
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
    .inactive {
      background: var(--color-bg);
      color: var(--color-text-muted);
    }
    .maintenance {
      background: var(--color-warning-soft);
      color: var(--color-warning);
    }
  `,
})
export class StatusBadge {
  readonly status = input.required<ResourceStatus>();
  protected readonly label = computed(() => LABELS[this.status()]);
}
