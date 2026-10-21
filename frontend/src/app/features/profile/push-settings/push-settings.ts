import { Component, OnInit, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { NotificationService } from '../../../core/services/notification.service';
import { PushService } from '../../../core/services/push.service';

/**
 * Notifications on this device (ADR-0056), on the user's profile: turned on or off here, one device
 * at a time, and tried with a test that goes to all of them. Says why when they can't be had: the
 * browser, its settings, an iPhone's Safari outside the Home Screen app, or a server without keys.
 */
@Component({
  selector: 'app-push-settings',
  styleUrl: './push-settings.scss',
  templateUrl: './push-settings.html',
})
export class PushSettings implements OnInit {
  private readonly push = inject(PushService);
  private readonly notifications = inject(NotificationService);

  protected readonly state = this.push.state;
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.push.refresh().catch(() => this.loadFailed.set(true));
  }

  protected async turnOn(): Promise<void> {
    this.busy.set(true);
    try {
      await this.push.turnOn();
      if (this.state() === 'on') {
        this.notifications.success(
          $localize`:@@push.turnedOn:Notifications are on for this device.`,
        );
      }
    } catch {
      this.notifications.error(
        $localize`:@@push.turnOnFailed:Notifications could not be turned on. Please try again.`,
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async turnOff(): Promise<void> {
    this.busy.set(true);
    try {
      await this.push.turnOff();
      this.notifications.success(
        $localize`:@@push.turnedOff:Notifications are off for this device.`,
      );
    } catch {
      this.notifications.error(
        $localize`:@@push.turnOffFailed:Notifications could not be turned off. Please try again.`,
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected test(): void {
    this.busy.set(true);
    this.push
      .sendTest()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe(({ devices }) =>
        this.notifications.success(
          devices === 0
            ? $localize`:@@push.testNone:None of your devices has notifications on.`
            : devices === 1
              ? $localize`:@@push.testSentOne:A test notification is on its way to your device.`
              : $localize`:@@push.testSentMany:A test notification is on its way to your ${devices}:count: devices.`,
        ),
      );
  }
}
