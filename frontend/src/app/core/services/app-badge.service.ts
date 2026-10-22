import { DOCUMENT } from '@angular/common';
import { Injectable, Injector, effect, inject } from '@angular/core';
import { InboxService } from './inbox.service';

/**
 * The number on the installed app's icon (ADR-0058): the unread messages the Inbox link counts,
 * on a phone's home screen or a computer's taskbar. While the app is open the icon follows that
 * count; while it's closed, SmartDesk's service worker sets it from each notification. Signed out,
 * the count is 0 and the icon shows none.
 */
@Injectable({ providedIn: 'root' })
export class AppBadgeService {
  private readonly inbox = inject(InboxService);
  private readonly navigator = inject(DOCUMENT).defaultView?.navigator;
  private readonly injector = inject(Injector);
  private started = false;

  /** Called once by the app; later calls, and browsers without the Badging API, do nothing. */
  start(): void {
    const navigator = this.navigator;
    if (
      this.started ||
      typeof navigator?.setAppBadge !== 'function' ||
      typeof navigator.clearAppBadge !== 'function'
    ) {
      return;
    }
    this.started = true;

    effect(
      () => {
        const unread = this.inbox.unread();
        // Refused where the browser won't badge this app - not installed, or an iPhone without
        // notifications allowed - which is nothing to tell anyone.
        (unread > 0 ? navigator.setAppBadge(unread) : navigator.clearAppBadge()).catch(
          () => undefined,
        );
      },
      { injector: this.injector },
    );
  }
}
