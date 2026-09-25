import { Injectable, signal } from '@angular/core';

export type NotificationKind = 'success' | 'info' | 'error';

export interface AppNotification {
  id: number;
  kind: NotificationKind;
  message: string;
}

const AUTO_DISMISS_MS = 5000;

/** App-wide toast messages, rendered by the NotificationHost component. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  private readonly notificationsSignal = signal<AppNotification[]>([]);
  readonly notifications = this.notificationsSignal.asReadonly();

  success(message: string): void {
    this.show('success', message);
  }

  /** `durationMs` keeps it up longer than usual, e.g. for a reminder. */
  info(message: string, durationMs = AUTO_DISMISS_MS): void {
    this.show('info', message, durationMs);
  }

  error(message: string): void {
    this.show('error', message);
  }

  dismiss(id: number): void {
    this.notificationsSignal.update((list) => list.filter((n) => n.id !== id));
  }

  private show(kind: NotificationKind, message: string, durationMs = AUTO_DISMISS_MS): void {
    // The same message twice in a row (e.g. several requests failing together) shows once.
    if (this.notificationsSignal().some((n) => n.kind === kind && n.message === message)) {
      return;
    }

    const id = this.nextId++;
    this.notificationsSignal.update((list) => [...list, { id, kind, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }
}
