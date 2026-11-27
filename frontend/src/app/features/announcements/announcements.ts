import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { announcementToneLabel } from '../../core/i18n/labels';
import {
  ANNOUNCEMENT_BODY_MAX,
  ANNOUNCEMENT_MAX_DAYS,
  ANNOUNCEMENT_TITLE_MAX,
  ANNOUNCEMENT_TONES,
  Announcement,
  AnnouncementTone,
} from '../../core/models/announcement.model';
import { ApiError } from '../../core/models/api-error.model';
import { AnnouncementService } from '../../core/services/announcement.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import {
  AnnouncementCard,
  AnnouncementView,
  announcementIcon,
} from '../../shared/components/announcement-card/announcement-card';
import { Icon } from '../../shared/components/icon/icon';

const DAY_MS = 24 * 60 * 60_000;

/** The last days a new announcement can be set to show until, a click away. */
const QUICK_DAYS = [
  { days: 0, label: () => $localize`:@@announcements.until.today:Today` },
  { days: 1, label: () => $localize`:@@announcements.until.tomorrow:Tomorrow` },
  { days: 6, label: () => $localize`:@@announcements.until.week:A week` },
  { days: 13, label: () => $localize`:@@announcements.until.twoWeeks:Two weeks` },
];

/** "2026-11-27": a day in the browser's time zone, as a date input has it. */
function isoDay(date: Date): string {
  return formatDate(date, 'yyyy-MM-dd', 'en-US');
}

function daysFromToday(days: number): string {
  const day = new Date();
  day.setDate(day.getDate() + days);
  return isoDay(day);
}

/**
 * The moment an announcement whose last day is `day` ends: when the next day starts, in the
 * browser's time zone, but no later than the API takes.
 */
export function endOfDay(day: string, now: number): string {
  const [year, month, date] = day.split('-').map(Number);
  const end = new Date(year, month - 1, date + 1).getTime();
  return new Date(Math.min(end, now + ANNOUNCEMENT_MAX_DAYS * DAY_MS)).toISOString();
}

/** From today until the last day the API takes. */
function dayInRange(control: AbstractControl<string>): ValidationErrors | null {
  const day = control.value;
  if (!day) return null;
  return day < daysFromToday(0) || day > daysFromToday(ANNOUNCEMENT_MAX_DAYS - 1)
    ? { range: true }
    : null;
}

/**
 * The office's announcements (`/admin/announcements`, ADR-0065), for Admins: one is written with a
 * title, maybe a text, its look and its last day, and seen as it will show on everyone's dashboard
 * while it's written. It can go to the phones with notifications on as well. Below, every one
 * posted: the ones showing and the ones that ended, each to take down.
 */
@Component({
  imports: [AnnouncementCard, Icon, ReactiveFormsModule, RouterLink],
  selector: 'app-announcements-admin',
  styleUrl: './announcements.scss',
  templateUrl: './announcements.html',
})
export class AnnouncementsAdmin implements OnInit {
  private readonly service = inject(AnnouncementService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);

  protected readonly maxTitle = ANNOUNCEMENT_TITLE_MAX;
  protected readonly maxBody = ANNOUNCEMENT_BODY_MAX;
  protected readonly tones = ANNOUNCEMENT_TONES;
  protected readonly quickDays = QUICK_DAYS.map((quick) => ({
    day: daysFromToday(quick.days),
    label: quick.label(),
  }));
  protected readonly firstDay = daysFromToday(0);
  protected readonly lastDay = daysFromToday(ANNOUNCEMENT_MAX_DAYS - 1);

  protected readonly announcements = signal<Announcement[] | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = new FormGroup({
    title: new FormControl('', {
      nonNullable: true,
      // Spaces alone aren't a title.
      validators: [
        Validators.required,
        Validators.pattern(/\S/),
        Validators.maxLength(ANNOUNCEMENT_TITLE_MAX),
      ],
    }),
    body: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(ANNOUNCEMENT_BODY_MAX)],
    }),
    tone: new FormControl<AnnouncementTone>('Info', { nonNullable: true }),
    lastDay: new FormControl(daysFromToday(6), {
      nonNullable: true,
      validators: [Validators.required, dayInRange],
    }),
    notify: new FormControl(false, { nonNullable: true }),
  });

  private readonly values = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  /** The announcement as it will show on the dashboard, as it's written. */
  protected readonly preview = computed<AnnouncementView>(() => {
    const { title, body, tone } = this.values();
    return {
      title: title.trim() || $localize`:@@announcements.previewTitle:Your announcement's title`,
      body: body.trim() || null,
      tone,
      authorName: this.authService.displayName(),
      createdAt: new Date().toISOString(),
    };
  });

  protected readonly bodyLength = computed(() => this.values().body.length);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.service.all().subscribe({
      next: (announcements) => this.announcements.set(announcements),
      error: () => this.loadFailed.set(true),
    });
  }

  protected toneLabel(tone: AnnouncementTone): string {
    return announcementToneLabel(tone);
  }

  protected icon(tone: AnnouncementTone) {
    return announcementIcon(tone);
  }

  protected titleError(): string | null {
    const control = this.form.controls.title;
    if (!control.touched || control.valid) return null;
    return control.hasError('maxlength')
      ? $localize`:@@announcements.titleTooLong:Use at most ${ANNOUNCEMENT_TITLE_MAX}:max: characters.`
      : $localize`:@@announcements.titleRequired:Give it a title.`;
  }

  protected lastDayError(): string | null {
    const control = this.form.controls.lastDay;
    if (!control.touched || control.valid) return null;
    const last = formatDate(this.lastDay, 'EEE d MMM', this.locale);
    return $localize`:@@announcements.lastDayRange:Pick a day from today to ${last}:last:.`;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { title, body, tone, lastDay, notify } = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set(null);
    this.service
      .create({
        title: title.trim(),
        body: body.trim() || null,
        tone,
        endsAt: endOfDay(lastDay, Date.now()),
        notify,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (posted) => {
          this.announcements.update((list) => [posted, ...(list ?? [])]);
          this.form.reset();
          this.notifications.success(
            notify
              ? $localize`:@@announcements.postedAndSent:Posted, and sent to the phones with notifications on.`
              : $localize`:@@announcements.posted:Posted: it's on everyone's dashboard now.`,
          );
        },
        error: (error: unknown) => {
          this.formError.set(
            error instanceof ApiError
              ? (Object.values(error.fieldErrors).flat()[0] ?? error.detail ?? error.message)
              : $localize`:@@announcements.postFailed:The announcement could not be posted. Please try again.`,
          );
        },
      });
  }

  protected remove(announcement: Announcement): void {
    const question = this.ended(announcement)
      ? $localize`:@@announcements.deleteConfirm:Delete ${announcement.title}:title:? It has ended already.`
      : $localize`:@@announcements.removeConfirm:Take ${announcement.title}:title: down? It goes from everyone's dashboard at once.`;
    if (!confirm(question)) return;
    this.service.remove(announcement.id).subscribe({
      next: () => {
        this.announcements.update((list) => (list ?? []).filter((a) => a.id !== announcement.id));
        this.notifications.success(
          $localize`:@@announcements.removed:${announcement.title}:title: was taken down.`,
        );
      },
      error: () => undefined,
    });
  }

  protected ended(announcement: Announcement): boolean {
    return new Date(announcement.endsAt).getTime() <= Date.now();
  }

  /**
   * "showing, last day Fri 4 Dec" for one that ends as a day does, as the ones posted here do;
   * "showing until Fri 4 Dec, 14:00" otherwise; "ended Mon 23 Nov" once it has.
   */
  protected when(announcement: Announcement): string {
    const end = new Date(announcement.endsAt);
    const atMidnight =
      end.getHours() === 0 &&
      end.getMinutes() === 0 &&
      end.getSeconds() === 0 &&
      end.getMilliseconds() === 0;
    const day = formatDate(atMidnight ? end.getTime() - 1 : end, 'EEE d MMM', this.locale);
    const time = formatDate(end, 'EEE d MMM, HH:mm', this.locale);
    if (this.ended(announcement)) {
      return atMidnight
        ? $localize`:@@announcements.endedOn:ended ${day}:day:`
        : $localize`:@@announcements.endedAt:ended ${time}:time:`;
    }
    return atMidnight
      ? $localize`:@@announcements.lastDayIs:showing, last day ${day}:day:`
      : $localize`:@@announcements.showingUntil:showing until ${time}:time:`;
  }
}
