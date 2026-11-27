import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { announcementToneLabel } from '../../core/i18n/labels';
import { LobbyAnnouncement } from '../../core/models/lobby.model';
import { announcementIcon } from '../../shared/components/announcement-card/announcement-card';
import { Icon } from '../../shared/components/icon/icon';

/** How long each announcement shows before the next, when there are several. */
export const LOBBY_NEWS_MS = 12_000;

/**
 * The announcements showing (ADR-0065) on a lobby screen: one at a time, newest first, each for
 * LOBBY_NEWS_MS, in its look's colour and icon and with its kind, as on the dashboard, big enough
 * to read across the lobby. Dots say how many there are, and which is up.
 */
@Component({
  imports: [Icon],
  selector: 'app-lobby-news',
  host: {
    role: 'region',
    '[attr.aria-label]': 'label',
    '[attr.data-tone]': 'current()?.announcement.tone',
  },
  template: `
    @if (current(); as current) {
      @for (announcement of [current.announcement]; track announcement.id) {
        <div class="news-item">
          <span class="news-icon"><app-icon [name]="icon(announcement.tone)" /></span>
          <div class="news-text">
            <span class="news-kind">{{ kind(announcement.tone) }}</span>
            <p class="news-title">{{ announcement.title }}</p>
            @if (announcement.body) {
              <p class="news-body">{{ announcement.body }}</p>
            }
          </div>
        </div>
      }
      @if (announcements().length > 1) {
        <span class="news-dots" aria-hidden="true">
          @for (item of announcements(); track item.id) {
            <span class="news-dot" [class.on]="$index === current.index"></span>
          }
        </span>
      }
    }
  `,
  styleUrl: './lobby-news.scss',
})
export class LobbyNews {
  readonly announcements = input.required<LobbyAnnouncement[]>();

  /** Which one is up, counting from the newest: one more every LOBBY_NEWS_MS. */
  private readonly turn = signal(0);

  protected readonly label = $localize`:@@lobby.announcements:Announcements`;
  protected readonly icon = announcementIcon;
  protected readonly kind = announcementToneLabel;
  protected readonly current = computed(() => {
    const announcements = this.announcements();
    if (announcements.length === 0) return null;
    const index = this.turn() % announcements.length;
    return { announcement: announcements[index], index };
  });

  constructor() {
    const timer = setInterval(() => this.turn.update((turn) => turn + 1), LOBBY_NEWS_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
