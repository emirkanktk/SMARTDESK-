import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CheckInCode, DoorScreenLink } from '../../../core/models/resource.model';
import { NotificationService } from '../../../core/services/notification.service';
import { ResourceService } from '../../../core/services/resource.service';
import { QrCode } from '../../../shared/components/qr-code/qr-code';

/**
 * A resource's QR code for checking in (ADR-0030), on its edit form: the code, its link, a
 * page to print it from, and a way to replace it when the printed one has got around. And a
 * screen at the door instead (ADR-0034): set up, a new link, or turned off.
 */
@Component({
  imports: [DatePipe, QrCode, RouterLink],
  selector: 'app-check-in-code',
  styleUrl: './check-in-code.scss',
  templateUrl: './check-in-code.html',
})
export class CheckInCodePanel implements OnInit {
  readonly resourceId = input.required<string>();
  readonly resourceName = input.required<string>();

  private readonly resourceService = inject(ResourceService);
  private readonly notifications = inject(NotificationService);

  protected readonly code = signal<CheckInCode | null>(null);
  protected readonly failed = signal(false);
  protected readonly replacing = signal(false);
  /** A door screen's link, just set up: shown this once, as only its hash is kept. */
  protected readonly screenLink = signal<DoorScreenLink | null>(null);
  protected readonly screenBusy = signal(false);
  protected readonly qrLabel = computed(
    () =>
      $localize`:@@checkInCode.qrLabel:QR code for checking in to ${this.resourceName()}:resource:`,
  );

  ngOnInit(): void {
    this.resourceService.getCheckInCode(this.resourceId()).subscribe({
      next: (code) => this.code.set(code),
      error: () => this.failed.set(true),
    });
  }

  protected replace(): void {
    const question = $localize`:@@checkInCode.replaceConfirm:Replace the QR code of "${this.resourceName()}:resource:"? The printed one stops working at once, so put the new one up in its place.`;
    if (!confirm(question)) {
      return;
    }

    this.replacing.set(true);
    this.resourceService
      .replaceCheckInCode(this.resourceId())
      .pipe(finalize(() => this.replacing.set(false)))
      .subscribe({
        next: (code) => {
          this.code.set(code);
          this.notifications.success(
            $localize`:@@checkInCode.replaced:The new QR code is ready. Print it and put it up in place of the old one.`,
          );
        },
        // The error interceptor has shown a toast already, and the old code still works.
        error: () => undefined,
      });
  }

  /**
   * A link for the screen at the door. Again, with one set up already: a new link, and the old
   * screen stops.
   */
  protected setUpScreen(): void {
    const replacing = this.code()?.doorScreenSince != null;
    if (
      replacing &&
      !confirm(
        $localize`:@@doorScreen.replaceConfirm:Make a new door screen link? The screen at the door stops working until it's opened with the new link.`,
      )
    ) {
      return;
    }

    this.screenBusy.set(true);
    this.resourceService
      .setUpDoorScreen(this.resourceId())
      .pipe(finalize(() => this.screenBusy.set(false)))
      .subscribe({
        next: (link) => {
          this.screenLink.set(link);
          this.code.update((code) => (code ? { ...code, doorScreenSince: link.setUpAt } : code));
        },
        error: () => undefined,
      });
  }

  /** Back to the printed code. */
  protected turnOffScreen(): void {
    if (
      !confirm(
        $localize`:@@doorScreen.turnOffConfirm:Turn off the door screen of "${this.resourceName()}:resource:"? The printed QR code checks in again, so put it up at the door.`,
      )
    ) {
      return;
    }

    this.screenBusy.set(true);
    this.resourceService
      .turnOffDoorScreen(this.resourceId())
      .pipe(finalize(() => this.screenBusy.set(false)))
      .subscribe({
        next: () => {
          this.screenLink.set(null);
          this.code.update((code) => (code ? { ...code, doorScreenSince: null } : code));
          this.notifications.success(
            $localize`:@@doorScreen.turnedOff:The door screen is off. The printed QR code checks in again.`,
          );
        },
        error: () => undefined,
      });
  }

  protected async copy(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.notifications.success($localize`:@@checkInCode.copied:Link copied.`);
    } catch {
      this.notifications.error($localize`:@@checkInCode.copyFailed:The link could not be copied.`);
    }
  }
}
