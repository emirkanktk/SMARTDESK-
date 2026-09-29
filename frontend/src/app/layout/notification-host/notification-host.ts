import { Component, inject } from '@angular/core';
import { NotificationService } from '../../core/services/notification.service';

/** Renders the NotificationService's toasts. Mounted once, in the root component. */
@Component({
  selector: 'app-notification-host',
  styleUrl: './notification-host.scss',
  template: `
    <div class="stack" aria-live="polite">
      @for (notification of notifications(); track notification.id) {
        <div class="toast" [class]="notification.kind" role="status">
          <span>{{ notification.message }}</span>
          <button
            type="button"
            class="close"
            aria-label="Dismiss"
            i18n-aria-label="@@toast.dismiss"
            (click)="dismiss(notification.id)"
          >
            &times;
          </button>
        </div>
      }
    </div>
  `,
})
export class NotificationHost {
  private readonly notificationService = inject(NotificationService);
  protected readonly notifications = this.notificationService.notifications;

  protected dismiss(id: number): void {
    this.notificationService.dismiss(id);
  }
}
