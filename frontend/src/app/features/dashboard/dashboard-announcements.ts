import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, interval, of, startWith, switchMap } from 'rxjs';
import { Announcement } from '../../core/models/announcement.model';
import { AnnouncementService } from '../../core/services/announcement.service';
import { AuthService } from '../../core/services/auth.service';
import { AnnouncementCard } from '../../shared/components/announcement-card/announcement-card';

/** How often the dashboard asks again for the announcements showing. */
export const ANNOUNCEMENTS_REFRESH_MS = 60_000;

/** Where the ones a person hid are kept on this device, after their account's id. */
export const HIDDEN_ANNOUNCEMENTS_KEY = 'smartdesk_hidden_announcements';

/**
 * The announcements showing now (ADR-0065), at the top of the dashboard, newest first and side by
 * side where there's room; on a phone, in a row to swipe through. Each can be hidden: the ones a
 * person hid stay hidden on this device until they end. Asked again every minute; one that fails
 * keeps what's shown.
 */
@Component({
  imports: [AnnouncementCard],
  selector: 'app-dashboard-announcements',
  template: `
    @if (shown().length > 0) {
      <section aria-labelledby="announcements-heading">
        <h2 id="announcements-heading" class="visually-hidden" i18n="@@announcements.title">
          Announcements
        </h2>
        <div class="list">
          @for (announcement of shown(); track announcement.id) {
            <app-announcement-card
              [announcement]="announcement"
              dismissible
              (dismissed)="hide(announcement)"
            />
          }
        </div>
      </section>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .list {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr));
      gap: var(--space-3);
      margin: 0 0 var(--space-4);
    }

    // On a phone, in a row to swipe through with the next one peeking in, rather than a screenful
    // of them above everything else.
    @media (max-width: 560px) {
      .list {
        grid-template-columns: none;
        grid-auto-columns: 86%;
        grid-auto-flow: column;
        padding-bottom: var(--space-1);
        overflow-x: auto;
        overscroll-behavior-x: contain;
        scroll-snap-type: x mandatory;
        scrollbar-width: thin;
      }

      .list:has(> :only-child) {
        grid-auto-columns: 100%;
      }

      app-announcement-card {
        scroll-snap-align: start;
      }
    }
  `,
})
export class DashboardAnnouncements {
  private readonly service = inject(AnnouncementService);
  private readonly authService = inject(AuthService);

  private readonly announcements = signal<Announcement[]>([]);
  private readonly hidden = signal<ReadonlySet<string>>(new Set(this.readHidden()));
  protected readonly shown = computed(() =>
    this.announcements().filter((announcement) => !this.hidden().has(announcement.id)),
  );

  constructor() {
    interval(ANNOUNCEMENTS_REFRESH_MS)
      .pipe(
        startWith(0),
        switchMap(() => this.service.current().pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((announcements) => {
        if (!announcements) return;
        this.announcements.set(announcements);
        // The ones that ended are forgotten, so what's kept stays short.
        const showing = new Set(announcements.map((announcement) => announcement.id));
        const kept = [...this.hidden()].filter((id) => showing.has(id));
        if (kept.length < this.hidden().size) this.keepHidden(kept);
      });
  }

  protected hide(announcement: Announcement): void {
    this.keepHidden([...this.hidden(), announcement.id]);
  }

  private keepHidden(ids: string[]): void {
    this.hidden.set(new Set(ids));
    const key = this.storageKey();
    if (!key) return;
    try {
      localStorage.setItem(key, JSON.stringify(ids));
    } catch {
      // Hidden until the page is left, then.
    }
  }

  private readHidden(): string[] {
    const key = this.storageKey();
    if (!key) return [];
    try {
      const kept: unknown = JSON.parse(localStorage.getItem(key) ?? '[]');
      return Array.isArray(kept) ? kept.filter((id): id is string => typeof id === 'string') : [];
    } catch {
      return [];
    }
  }

  private storageKey(): string | null {
    const id = this.authService.currentUser()?.id;
    return id ? `${HIDDEN_ANNOUNCEMENTS_KEY}_${id}` : null;
  }
}
