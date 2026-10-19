import { Component, inject } from '@angular/core';
import { AppUpdateService } from '../../core/services/app-update.service';
import { ConnectivityService } from '../../core/services/connectivity.service';
import { NotificationService } from '../../core/services/notification.service';

/**
 * Renders the NotificationService's toasts, and above them what the app says about itself:
 * that it's offline, or that a new version is ready (ADR-0054). Mounted once, in the root
 * component.
 */
@Component({
  selector: 'app-notification-host',
  styleUrl: './notification-host.scss',
  template: `
    <div class="stack" aria-live="polite">
      @if (!online()) {
        <div class="toast offline" role="status">
          <span i18n="@@appStatus.offline"
            >You're offline. What's on screen may be out of date, and nothing can be booked or
            changed until you're back.</span
          >
        </div>
      }
      @if (updateReady()) {
        <div class="toast info update" role="status">
          <span i18n="@@appStatus.updateReady">A new version of SmartDesk is ready.</span>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            (click)="reload()"
            i18n="@@appStatus.reload"
          >
            Reload
          </button>
          <button
            type="button"
            class="close"
            aria-label="Dismiss"
            i18n-aria-label="@@toast.dismiss"
            (click)="later()"
          >
            &times;
          </button>
        </div>
      }
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
  private readonly appUpdate = inject(AppUpdateService);
  protected readonly notifications = this.notificationService.notifications;
  protected readonly online = inject(ConnectivityService).online;
  protected readonly updateReady = this.appUpdate.ready;

  protected dismiss(id: number): void {
    this.notificationService.dismiss(id);
  }

  protected reload(): void {
    void this.appUpdate.reload();
  }

  protected later(): void {
    this.appUpdate.later();
  }
}
