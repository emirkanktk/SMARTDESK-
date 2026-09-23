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

  info(message: string): void {
    this.show('info', message);
  }

  error(message: string): void {
    this.show('error', message);
  }

  dismiss(id: number): void {
    this.notificationsSignal.update((list) => list.filter((n) => n.id !== id));
  }

  private show(kind: NotificationKind, message: string): void {
    // The same message twice in a row (e.g. several requests failing together) shows once.
    if (this.notificationsSignal().some((n) => n.kind === kind && n.message === message)) {
      return;
    }

    const id = this.nextId++;
    this.notificationsSignal.update((list) => [...list, { id, kind, message }]);
    setTimeout(() => this.dismiss(id), AUTO_DISMISS_MS);
  }
}
