import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { SignInSession } from '../../../core/models/session.model';
import { NotificationService } from '../../../core/services/notification.service';
import { SessionService } from '../../../core/services/session.service';
import { DeviceDescription, describeUserAgent } from '../../../core/utils/user-agent';
import { Icon, IconName } from '../../../shared/components/icon/icon';

const DEVICE_ICONS: Record<DeviceDescription['kind'], IconName> = {
  phone: 'phone',
  tablet: 'tablet',
  computer: 'laptop',
};

/**
 * The devices the user is signed in on (ADR-0061), on their profile: this one first, then the
 * others by their last use, each with its browser and how it signed in. One that's lost, shared or
 * not recognised is signed out from here, or every other one at once; SmartDesk open there goes to
 * the sign-in page straight away.
 */
@Component({
  selector: 'app-sessions',
  imports: [DatePipe, Icon],
  styleUrl: './sessions.scss',
  templateUrl: './sessions.html',
})
export class Sessions implements OnInit {
  private readonly service = inject(SessionService);
  private readonly notifications = inject(NotificationService);

  protected readonly sessions = signal<SignInSession[] | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly hasOthers = computed(() => this.sessions()?.some((s) => !s.current) ?? false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.service.list().subscribe({
      next: (sessions) => this.sessions.set(sessions),
      error: () => this.loadFailed.set(true),
    });
  }

  /** "Safari · iPhone": the browser and what it runs on, whichever of them is known. */
  protected deviceName(session: SignInSession): string {
    const { browser, system } = describeUserAgent(session.userAgent);
    return (
      [browser, system].filter(Boolean).join(' · ') ||
      $localize`:@@sessions.unknownDevice:Unknown browser`
    );
  }

  protected iconFor(session: SignInSession): IconName {
    return DEVICE_ICONS[describeUserAgent(session.userAgent).kind];
  }

  protected endLabel(session: SignInSession): string {
    return $localize`:@@sessions.endLabel:Sign out ${this.deviceName(session)}:device:`;
  }

  protected end(session: SignInSession): void {
    const device = this.deviceName(session);
    if (
      !confirm(
        $localize`:@@sessions.endConfirm:Sign out ${device}:device:? It has to sign in again to use SmartDesk.`,
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.service
      .end(session.id)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.drop(session);
          this.notifications.success($localize`:@@sessions.ended:Signed out of ${device}:device:.`);
        },
        error: (error: unknown) => {
          // Over already: signed out there, or ended from yet another device.
          if (error instanceof ApiError && error.status === 404) {
            this.drop(session);
            return;
          }
          this.notifications.error(
            error instanceof ApiError && error.status === 0
              ? error.title
              : $localize`:@@sessions.endFailed:That device could not be signed out. Please try again.`,
          );
        },
      });
  }

  protected endOthers(): void {
    if (
      !confirm(
        $localize`:@@sessions.endOthersConfirm:Sign out every other device? Each has to sign in again; this one stays signed in.`,
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.service
      .endOthers()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: ({ ended }) => {
          this.sessions.update((sessions) => sessions?.filter((s) => s.current) ?? null);
          this.notifications.success(
            ended === 1
              ? $localize`:@@sessions.endedOne:Signed out of 1 other device.`
              : $localize`:@@sessions.endedOthers:Signed out of ${ended}:count: other devices.`,
          );
        },
        error: (error: unknown) =>
          this.notifications.error(
            error instanceof ApiError && error.status === 0
              ? error.title
              : $localize`:@@sessions.endOthersFailed:The other devices could not be signed out. Please try again.`,
          ),
      });
  }

  private drop(session: SignInSession): void {
    this.sessions.update((sessions) => sessions?.filter((s) => s.id !== session.id) ?? null);
  }
}
