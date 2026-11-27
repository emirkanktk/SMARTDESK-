import { formatDate } from '@angular/common';
import {
  Component,
  LOCALE_ID,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { announcementToneLabel } from '../../../core/i18n/labels';
import { Announcement, AnnouncementTone } from '../../../core/models/announcement.model';
import { Icon, IconName } from '../icon/icon';

/** What a card shows: an announcement, or one still being written. */
export type AnnouncementView = Pick<Announcement, 'title' | 'body' | 'tone'> &
  Partial<Pick<Announcement, 'authorName' | 'createdAt'>>;

/** A text this long, or of this many lines, shows its start until it's asked for in full. */
const LONG_TEXT = 240;
const LONG_LINES = 4;

/** Each look's icon, beside its words: never the colour alone. */
export function announcementIcon(tone: AnnouncementTone): IconName {
  switch (tone) {
    case 'Good':
      return 'sparkles';
    case 'Warning':
      return 'alert';
    default:
      return 'megaphone';
  }
}

/**
 * An announcement as everyone sees it on their dashboard (ADR-0065), in its look's colour and icon:
 * what kind it is, its title and text, and who posted it when. A long text shows its start, with a
 * button for the rest; `dismissible` adds one that hides it.
 */
@Component({
  imports: [Icon],
  selector: 'app-announcement-card',
  host: { '[attr.data-tone]': 'announcement().tone' },
  styleUrl: './announcement-card.scss',
  templateUrl: './announcement-card.html',
})
export class AnnouncementCard {
  private readonly locale = inject(LOCALE_ID);

  readonly announcement = input.required<AnnouncementView>();
  readonly dismissible = input(false, { transform: booleanAttribute });
  readonly dismissed = output<void>();

  protected readonly expanded = signal(false);
  protected readonly icon = computed(() => announcementIcon(this.announcement().tone));
  protected readonly kind = computed(() => announcementToneLabel(this.announcement().tone));
  protected readonly long = computed(() => {
    const body = this.announcement().body ?? '';
    return body.length > LONG_TEXT || body.split('\n').length > LONG_LINES;
  });
  protected readonly meta = computed(() => {
    const { authorName, createdAt } = this.announcement();
    return [authorName, createdAt ? formatDate(createdAt, 'EEE d MMM', this.locale) : null]
      .filter((part): part is string => !!part)
      .join(' · ');
  });
  protected readonly hideLabel = computed(
    () => $localize`:@@announcement.hide:Hide ${this.announcement().title}:title:`,
  );
}
