import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { CalendarSubscription } from '../../../core/models/calendar.model';
import { CalendarService } from '../../../core/services/calendar.service';
import { NotificationService } from '../../../core/services/notification.service';

/**
 * The user's personal calendar link (ADR-0048), on their profile: a calendar app subscribed to it
 * shows their bookings and the meetings they're invited to, and keeps up with them. Made here,
 * shown once - only its hash is kept - and replaced or removed here when it has got out.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-calendar-link',
  styleUrl: './calendar-link.scss',
  templateUrl: './calendar-link.html',
})
export class CalendarLink implements OnInit {
  private readonly calendar = inject(CalendarService);
  private readonly notifications = inject(NotificationService);

  protected readonly subscription = signal<CalendarSubscription | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  /** The link just made: shown this once. */
  protected readonly link = signal<string | null>(null);

  /** The same address for a calendar app to open: webcal:// asks it to subscribe. */
  protected readonly webcal = computed(() => this.link()?.replace(/^https?:/, 'webcal:') ?? '');
  protected readonly google = computed(
    () => `https://calendar.google.com/calendar/r?cid=${encodeURIComponent(this.webcal())}`,
  );
  /** Google's and Microsoft's servers fetch the link themselves, so they need it on the internet. */
  protected readonly offTheInternet = computed(() => {
    const link = this.link();
    return link !== null && !onTheInternet(link);
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.calendar
      .getSubscription()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (subscription) => this.subscription.set(subscription),
        error: () => this.loadFailed.set(true),
      });
  }

  /** A link, or a new one in place of the old, which stops working. */
  protected create(): void {
    if (
      this.subscription() &&
      !confirm(
        $localize`:@@calendarLink.replaceConfirm:Make a new calendar link? The old one stops working, so a calendar subscribed to it stops updating until it's given the new one.`,
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.calendar
      .createSubscription()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (subscription) => {
          this.link.set(subscription.url);
          this.subscription.set({ ...subscription, url: null });
        },
        // The error interceptor has shown a toast already.
        error: () => undefined,
      });
  }

  protected remove(): void {
    if (
      !confirm(
        $localize`:@@calendarLink.removeConfirm:Remove your calendar link? A calendar subscribed to it stops updating.`,
      )
    ) {
      return;
    }

    this.busy.set(true);
    this.calendar
      .deleteSubscription()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.subscription.set(null);
          this.link.set(null);
          this.notifications.success(
            $localize`:@@calendarLink.removed:Your calendar link is removed.`,
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

/** Not this computer, nor an address only a local network reaches. */
export function onTheInternet(link: string): boolean {
  const host = new URL(link).hostname;
  return !(
    host === 'localhost' ||
    host === '[::1]' ||
    /^127\./.test(host) ||
    /^10\./.test(host) ||
    /^192\.168\./.test(host) ||
    /^172\.(1[6-9]|2\d|3[01])\./.test(host)
  );
}
