import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/**
 * Whether the browser has a network (ADR-0054). The installed app opens without one, from what
 * its service worker keeps, but can't ask the API anything meanwhile, and says so.
 */
@Injectable({ providedIn: 'root' })
export class ConnectivityService {
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly onlineState = signal(this.window?.navigator.onLine ?? true);

  readonly online = this.onlineState.asReadonly();

  constructor() {
    const window = this.window;
    if (!window) {
      return;
    }

    const online = () => this.onlineState.set(true);
    const offline = () => this.onlineState.set(false);
    window.addEventListener('online', online);
    window.addEventListener('offline', offline);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('online', online);
      window.removeEventListener('offline', offline);
    });
  }
}
