import { DOCUMENT, DatePipe, formatDate } from '@angular/common';
import {
  Component,
  DestroyRef,
  InjectionToken,
  LOCALE_ID,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  map,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  BookingSuggestions,
  SuggestedResource,
  SuggestedTime,
  bookingSuggestions,
} from '../../core/models/booking-suggestions.model';
import {
  AttendeeResponse,
  CHECK_IN,
  CreateSeriesRequest,
  DEFAULT_REMINDER_MINUTES,
  Invitee,
  MAX_ATTENDEES,
  REMINDER_OPTIONS,
  RESERVATION_LIMITS,
  RESERVATION_OVERLAPS,
  RESOURCE_BLOCKED,
  Recurrence,
  Reservation,
  ReservationSeries,
  SERIES_CONFLICTS,
  SeriesDate,
  SeriesPreviewQuery,
  SeriesResult,
  UpdateReservationRequest,
  WEEK_DAYS,
  WeekDay,
} from '../../core/models/reservation.model';
import { Resource } from '../../core/models/resource.model';
import { BookingRules } from '../../core/models/booking-rules.model';
import { AuthService } from '../../core/services/auth.service';
import { BookingRulesService } from '../../core/services/booking-rules.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReminderAlertsService } from '../../core/services/reminder-alerts.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { WaitlistService } from '../../core/services/waitlist.service';
import { MeetingAnswer } from '../../shared/components/meeting-answer/meeting-answer';
import { ResourcePhoto } from '../../shared/components/resource-photo/resource-photo';
import { SeriesDates } from './series-dates/series-dates';
import {
  addDays,
  addMinutes,
  floorToQuarterHour,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseDateParam,
  parseIso,
  startOfDay,
  toDateParam,
  toDateTimeLocal,
} from '../../shared/utils/date-time';
import { keepDurationWhenStartMoves } from '../../shared/utils/keep-duration';
import { RuleProblems, describeRules, ruleProblems } from '../../shared/utils/booking-rules';
import {
  WORKING_DAYS,
  browserTimeZone,
  dayName,
  describeSeries,
  weekDayOf,
} from '../../shared/utils/recurrence';
import { searchKey } from '../../shared/utils/search';
import { applyServerErrors } from '../../shared/utils/server-errors';
import {
  DateRangeOptions,
  RANGE_MESSAGES,
  dateRangeValidator,
} from '../../shared/validators/date-range.validator';
import {
  attendeeResponseLabel,
  reservationStatusLabel,
  resourceTypeLabel,
} from '../../core/i18n/labels';

const L = RESERVATION_LIMITS;
const DEFAULT_DURATION_MIN = 60;
const RANGE_FORMAT = 'EEE d MMM, HH:mm';
/** From this many people on, the list of who to invite gets a search box. */
export const ATTENDEE_SEARCH_FROM = 8;
/** How a new booking repeats, as the form offers it (ADR-0035). */
export type RepeatChoice = 'none' | 'daily' | 'weekdays' | 'weekly' | 'biweekly';
/** How long a new series runs unless the booker picks another last day. */
export const SERIES_DEFAULT_DAYS = 28;
/** How long the form waits after a change before asking which dates are free, in ms. */
export const SERIES_PREVIEW_DEBOUNCE = new InjectionToken<number>('SERIES_PREVIEW_DEBOUNCE', {
  factory: () => 300,
});

/** The dates a repeating booking would have, as the form knows them. */
type SeriesPreviewState =
  | { state: 'idle' }
  | { state: 'loading' }
  | { state: 'ready'; dates: SeriesDate[] }
  | { state: 'invalid'; message: string }
  | { state: 'failed' };

/**
 * What the form allows, following the API's update rules. `inProgress`: started, not ended -
 * only the end and the note may change. `readOnly`: ended, cancelled, or someone else's.
 */
type Mode = 'create' | 'upcoming' | 'inProgress' | 'readOnly';

interface ResourceOption {
  id: string;
  name: string;
  resourceTypeId: string | null;
  typeName: string;
  location: string;
  capacity: number | null;
  features: string[];
  photoUpdatedAt: string | null;
  /** Its bookings must be checked in at the door (ADR-0030). */
  checkInRequired: boolean;
}

/** Someone who can be on the booking's list of who's invited (ADR-0032). */
interface Person {
  id: string;
  name: string;
  /** Null for someone already on the list whom the caller may no longer invite. */
  email: string | null;
  /** Their answer, for someone already on the list; null for anyone not invited yet. */
  response: AttendeeResponse | null;
}

/**
 * Book a resource (`/reservations/new`) or change a booking (`/reservations/:id/edit`). Managers
 * and Admins can invite people to it, who get a message in their inbox (ADR-0032). A new booking
 * can repeat; the form then shows its dates, and which are free, before booking them all
 * (ADR-0035). Anyone else who opens a booking, e.g. someone invited to it, sees it read-only.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe, MeetingAnswer, ResourcePhoto, SeriesDates],
  selector: 'app-reservation-form',
  styleUrl: './reservation-form.scss',
  templateUrl: './reservation-form.html',
})
export class ReservationForm implements OnInit {
  private readonly reservationService = inject(ReservationService);
  private readonly resourceService = inject(ResourceService);
  private readonly authService = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly reminderAlerts = inject(ReminderAlertsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly locale = inject(LOCALE_ID);
  private readonly document = inject(DOCUMENT);
  private readonly waitlist = inject(WaitlistService);
  private readonly bookingRulesService = inject(BookingRulesService);

  /** Route parameter; absent when booking. */
  readonly id = input<string>();
  // Query parameters from the availability search's Book link; times are UTC ISO strings.
  readonly resourceId = input<string>();
  readonly start = input<string>();
  readonly end = input<string>();

  protected readonly limits = L;
  protected readonly checkIn = CHECK_IN;
  protected readonly reminderOptions = REMINDER_OPTIONS;
  /** Desktop notifications turned off in the browser: the reminder then only shows in the app. */
  protected readonly desktopBlocked = this.reminderAlerts.permission() === 'denied';

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      resourceId: ['', Validators.required],
      startDateTime: ['', Validators.required],
      endDateTime: ['', Validators.required],
      note: ['', Validators.maxLength(L.noteMaxLength)],
      reminderMinutes: [DEFAULT_REMINDER_MINUTES as number | null],
      attendeeIds: [[] as string[]],
      // A new booking only: how it repeats (ADR-0035).
      repeat: ['none' as RepeatChoice],
      repeatDays: [[] as WeekDay[]],
      until: [''],
    },
    { validators: dateRangeValidator(rangeRules('create')) },
  );

  protected readonly mode = signal<Mode>('create');
  /** The reservation being changed; null when booking. */
  protected readonly reservation = signal<Reservation | null>(null);
  protected readonly readOnlyReason = signal<string | null>(null);
  protected readonly statusLabel = reservationStatusLabel;
  protected readonly typeLabel = resourceTypeLabel;
  protected readonly responseLabel = attendeeResponseLabel;
  /** Who booked it, when an admin is changing someone else's reservation. */
  protected readonly bookedBy = computed(() => {
    const reservation = this.reservation();
    return reservation && reservation.userId !== this.authService.currentUser()?.id
      ? (reservation.userDisplayName ?? $localize`:@@reservationForm.anotherUser:another user`)
      : null;
  });

  private readonly activeResources = signal<ResourceOption[]>([]);
  /**
   * Active resources, plus - when editing - the reservation's own resource even if it is no
   * longer Active, so a note can still be changed after it went into maintenance.
   */
  protected readonly resourceOptions = computed(() => {
    const active = this.activeResources();
    const reservation = this.reservation();
    if (!reservation || active.some((r) => r.id === reservation.resourceId)) {
      return active;
    }
    return [
      ...active,
      {
        id: reservation.resourceId,
        name: $localize`:@@reservationForm.notBookable:${reservation.resourceName}:resource: (not bookable right now)`,
        resourceTypeId: null,
        typeName: reservation.resourceTypeName,
        location: reservation.resourceLocation,
        capacity: null,
        features: [],
        photoUpdatedAt: null,
        checkInRequired: reservation.checkInRequired,
      },
    ];
  });
  protected readonly resourceGroups = computed(() => groupByType(this.resourceOptions()));

  /** Who the caller may invite; only Managers and Admins ask. */
  private readonly invitees = signal<Invitee[]>([]);
  protected readonly inviteesFailed = signal(false);
  protected readonly attendeeSearch = signal('');
  protected readonly attendeeSearchFrom = ATTENDEE_SEARCH_FROM;

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly cancelling = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** A 409 from the API, e.g. the slot was taken meanwhile. Its detail is shown as-is. */
  protected readonly conflict = signal<string | null>(null);
  /** What the API offered instead with it: other times and resources (ADR-0042). */
  protected readonly suggestions = signal<BookingSuggestions | null>(null);
  /** The start asked for when the offer came, so times on that day read as times only. */
  private askedStart: Date | null = null;
  /** The time asked for is taken or blocked: it can be waited for (ADR-0043). */
  protected readonly mayWait = signal(false);
  protected readonly joining = signal(false);

  // getRawValue: a locked (disabled) start or resource still counts for the hints.
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );
  protected readonly selectedResource = computed(() =>
    this.resourceOptions().find((r) => r.id === this.formValue().resourceId),
  );

  /** The rules bookings keep to (ADR-0046), once loaded; the API keeps to them either way. */
  private readonly bookingRules = signal<BookingRules | null>(null);
  /** The chosen resource's kind's own rules. */
  private readonly kindRules = computed(() => {
    const resource = this.selectedResource();
    if (!resource) return null;
    return (
      this.bookingRules()?.types.find((kind) =>
        resource.resourceTypeId
          ? kind.resourceTypeId === resource.resourceTypeId
          : kind.name === resource.typeName,
      ) ?? null
    );
  });
  /**
   * A booking of the chosen kind waits for an Admin's approval (ADR-0049): it's asked for, not
   * booked yet, and doesn't repeat. An Admin's own bookings don't wait.
   */
  protected readonly needsApproval = computed(
    () => this.kindRules()?.requiresApproval === true && !this.authService.isAdmin(),
  );
  /** "Desk bookings: within office hours (Mon-Fri 09:00-18:00), at most 9 h long." */
  protected readonly rulesHint = computed(() => {
    const kind = this.kindRules();
    const office = this.bookingRules()?.officeHours;
    const rules = kind && office ? describeRules(kind, office, this.needsApproval()) : [];
    if (!kind || rules.length === 0) return null;
    const name = resourceTypeLabel(kind.name);
    const list = rules.join(', ');
    return $localize`:@@rules.hint:${name}:kind: bookings: ${list}:rules:.`;
  });
  /**
   * What the kind's rules say against the times chosen, before the API is asked. Nothing for a
   * change that claims nothing new - the same resource, within the booking's own time - which
   * the API allows whatever the rules are now.
   */
  protected readonly ruleProblems = computed((): RuleProblems => {
    const none: RuleProblems = { start: null, end: null };
    const kind = this.kindRules();
    const office = this.bookingRules()?.officeHours;
    const value = this.formValue();
    if (!kind || !office || this.mode() === 'readOnly') return none;
    if (!fromDateTimeLocal(value.startDateTime) || !fromDateTimeLocal(value.endDateTime)) {
      return none;
    }
    const reservation = this.reservation();
    const start = new Date(toIso(value.startDateTime, reservation?.startDateTime));
    const end = new Date(toIso(value.endDateTime, reservation?.endDateTime));
    if (end <= start) return none;
    const withinItsOwn =
      !!reservation &&
      value.resourceId === reservation.resourceId &&
      start >= new Date(reservation.startDateTime) &&
      end <= new Date(reservation.endDateTime);
    return withinItsOwn ? none : ruleProblems(kind, office, start, end, new Date());
  });
  /** Live duration hint under the end field. */
  protected readonly duration = computed(() => {
    const start = fromDateTimeLocal(this.formValue().startDateTime);
    const end = fromDateTimeLocal(this.formValue().endDateTime);
    return start && end && end > start ? formatDuration(start, end) : null;
  });
  protected readonly noteLength = computed(() => this.formValue().note.length);

  /**
   * Everyone who can be on the list: the people the caller may invite, and whoever is on it
   * already, even if they may no longer be invited - they can stay, or be taken off. Never the
   * booking's owner, who is there anyway.
   */
  protected readonly people = computed(() => {
    const reservation = this.reservation();
    const ownerId = reservation?.userId ?? this.authService.currentUser()?.id;
    const byId = new Map<string, Person>();
    for (const attendee of reservation?.attendees ?? []) {
      const name = attendee.name ?? $localize`:@@common.unknownUser:Unknown user`;
      byId.set(attendee.id, { id: attendee.id, name, email: null, response: attendee.response });
    }
    for (const invitee of this.invitees()) {
      byId.set(invitee.id, { ...invitee, response: byId.get(invitee.id)?.response ?? null });
    }
    if (ownerId) {
      byId.delete(ownerId);
    }
    return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name, this.locale));
  });
  protected readonly visiblePeople = computed(() => {
    const term = searchKey(this.attendeeSearch().trim());
    return term
      ? this.people().filter(
          (p) =>
            searchKey(p.name).includes(term) || (!!p.email && searchKey(p.email).includes(term)),
        )
      : this.people();
  });
  protected readonly selectedAttendees = computed(() => new Set(this.formValue().attendeeIds));
  /** Someone invited to this meeting, who can answer it here: it's still on and not over. */
  protected readonly canAnswer = computed(() => {
    const reservation = this.reservation();
    const userId = this.authService.currentUser()?.id;
    return (
      !!reservation &&
      reservation.status !== 'Cancelled' &&
      new Date(reservation.endDateTime).getTime() > Date.now() &&
      reservation.attendees.some((a) => a.id === userId)
    );
  });
  /** The resource holds fewer people than the meeting would have, the owner included. */
  private readonly roomTooSmall = computed(() => {
    const resource = this.selectedResource();
    const people = this.formValue().attendeeIds.length + 1;
    return resource?.capacity != null && people > resource.capacity
      ? { name: resource.name, capacity: resource.capacity, people }
      : null;
  });

  /** Earliest value offered by the start picker, on the quarter-hour grid its step expects. */
  protected readonly minStart = toDateTimeLocal(floorToQuarterHour());

  protected readonly weekDays = WEEK_DAYS;
  /** The latest last day the date picker offers: as far ahead as a booking may start. */
  protected readonly maxUntil = toDateParam(addDays(startOfDay(new Date()), L.maxAdvanceDays));
  private readonly formStatus = toSignal(this.form.statusChanges, {
    initialValue: this.form.status,
  });
  /** The day the booking starts on, which a weekly series has to include. */
  protected readonly startDay = computed(() => {
    const start = fromDateTimeLocal(this.formValue().startDateTime);
    return start ? weekDayOf(start) : null;
  });
  /** The first day a series may end on: the day it starts. */
  protected readonly minUntil = computed(() => {
    const start = fromDateTimeLocal(this.formValue().startDateTime);
    return start ? toDateParam(start) : null;
  });
  /** The rule as the API takes it; null when the booking doesn't repeat. */
  protected readonly recurrence = computed<Recurrence | null>(() => {
    const value = this.formValue();
    const startDay = this.startDay();
    if (this.mode() !== 'create' || value.repeat === 'none' || !startDay || this.needsApproval()) {
      return null;
    }
    const weekly = value.repeat === 'weekly' || value.repeat === 'biweekly';
    return {
      frequency: value.repeat === 'daily' ? 'Daily' : 'Weekly',
      interval: value.repeat === 'biweekly' ? 2 : 1,
      daysOfWeek:
        value.repeat === 'weekdays'
          ? [...WORKING_DAYS]
          : weekly
            ? WEEK_DAYS.filter((day) => day === startDay || value.repeatDays.includes(day))
            : [],
      until: value.until,
      timeZone: browserTimeZone(),
    };
  });
  /** What's wrong with how it repeats, found before asking the API. */
  protected readonly repeatError = computed(() => {
    const value = this.formValue();
    if (!this.recurrence()) return null;
    const start = fromDateTimeLocal(value.startDateTime);
    if (value.repeat === 'weekdays' && start && !WORKING_DAYS.includes(weekDayOf(start))) {
      return $localize`:@@series.weekdayStart:Start on a weekday for a booking that repeats on weekdays.`;
    }
    const until = parseDateParam(value.until);
    if (!until) {
      return $localize`:@@series.chooseUntil:Choose the last day it may repeat on.`;
    }
    if (start && until < startOfDay(start)) {
      return $localize`:@@series.untilBeforeStart:The last day can't be before the first.`;
    }
    if (value.until > this.maxUntil) {
      return $localize`:@@series.untilTooFar:A booking can repeat at most ${L.maxAdvanceDays}:days: days ahead.`;
    }
    return null;
  });
  /** Asks again for the same dates, e.g. after booking found one taken meanwhile. */
  private readonly previewRun = signal(0);
  private readonly seriesQuery = computed<SeriesPreviewQuery | null>(() => {
    const recurrence = this.recurrence();
    const value = this.formValue();
    const start = fromDateTimeLocal(value.startDateTime);
    const end = fromDateTimeLocal(value.endDateTime);
    if (!recurrence || this.repeatError() || this.formStatus() !== 'VALID' || !start || !end) {
      return null;
    }
    return {
      resourceId: value.resourceId,
      startDateTime: start.toISOString(),
      endDateTime: end.toISOString(),
      recurrence,
    };
  });
  /** The dates of the series and which are free, asked for as the form changes. */
  protected readonly seriesPreview = toSignal(
    toObservable(computed(() => ({ query: this.seriesQuery(), run: this.previewRun() }))).pipe(
      debounceTime(inject(SERIES_PREVIEW_DEBOUNCE)),
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      switchMap(({ query }) =>
        query
          ? this.reservationService.previewSeries(query).pipe(
              map((preview): SeriesPreviewState => ({ state: 'ready', dates: preview.dates })),
              catchError((error: unknown) => of(previewProblem(error))),
              startWith<SeriesPreviewState>({ state: 'loading' }),
            )
          : of<SeriesPreviewState>({ state: 'idle' }),
      ),
      takeUntilDestroyed(),
    ),
    { initialValue: { state: 'idle' } as SeriesPreviewState },
  );
  /** How many of the series' dates are free, once the API has said. */
  protected readonly seriesCounts = computed(() => {
    const preview = this.seriesPreview();
    if (preview.state !== 'ready') return null;
    const free = preview.dates.filter((date) => date.free).length;
    return { total: preview.dates.length, free, taken: preview.dates.length - free };
  });

  ngOnInit(): void {
    if (this.authService.isManager() || this.authService.isAdmin()) {
      this.reservationService.getInvitees().subscribe({
        next: (invitees) => this.invitees.set(invitees),
        error: () => this.inviteesFailed.set(true),
      });
    }

    // Shown, and kept to before asking; without them the API still says what's wrong.
    this.bookingRulesService
      .get(true)
      .pipe(catchError(() => of(null)))
      .subscribe((rules) => this.bookingRules.set(rules));

    this.resourceService.getResources({ status: 'Active' }).subscribe({
      next: (resources) => this.activeResources.set(resources.map(toOption)),
      error: () =>
        this.formError.set(
          $localize`:@@reservationForm.resourcesFailed:The list of resources could not be loaded.`,
        ),
    });

    const id = this.id();
    if (id) {
      this.load(id);
    } else {
      this.prefillFromLink();
    }
  }

  /** Query parameters that take the user back to the availability search they came from. */
  protected searchParams(): Record<string, string> | null {
    const start = this.start();
    const end = this.end();
    return start && end ? { start, end } : null;
  }

  /** Availability search for the chosen time, offered when the slot turned out to be taken. */
  protected alternativesParams(): Record<string, string> | null {
    const { startDateTime, endDateTime } = this.form.getRawValue();
    const start = fromDateTimeLocal(startDateTime);
    const end = fromDateTimeLocal(endDateTime);
    if (!start || !end) {
      return null;
    }
    const typeId = this.selectedResource()?.resourceTypeId;
    return {
      start: start.toISOString(),
      end: end.toISOString(),
      ...(typeId ? { type: typeId } : {}),
    };
  }

  /** A free time the API offered: the form takes it, and booking it is the user's next click. */
  protected useTime(time: SuggestedTime): void {
    this.form.patchValue({
      startDateTime: toDateTimeLocal(new Date(time.start)),
      endDateTime: toDateTimeLocal(new Date(time.end)),
    });
    this.clearConflict();
    this.document.getElementById('start')?.focus();
  }

  /** A resource free at that time, as the API offered it: the form takes it instead. */
  protected useResource(resource: SuggestedResource): void {
    this.form.controls.resourceId.setValue(resource.id);
    this.clearConflict();
    this.document.getElementById('resource')?.focus();
  }

  /**
   * Waits for the time asked for (ADR-0043): if it frees up, it's booked for the user, and
   * their inbox says so. On to My reservations, where it's listed.
   */
  protected joinWaitlist(): void {
    const value = this.form.getRawValue();
    const start = fromDateTimeLocal(value.startDateTime);
    const end = fromDateTimeLocal(value.endDateTime);
    if (!start || !end) {
      return;
    }

    this.joining.set(true);
    this.waitlist
      .join({
        resourceId: value.resourceId,
        startDateTime: start.toISOString(),
        endDateTime: end.toISOString(),
      })
      .pipe(finalize(() => this.joining.set(false)))
      .subscribe({
        next: (entry) => {
          this.notifications.success(
            $localize`:@@reservationForm.waiting:You're on the waiting list for ${entry.resourceName}:resource:, ${this.formatRange(entry)}:when:. If it frees up, it's booked for you.`,
          );
          void this.router.navigateByUrl('/reservations/mine');
        },
        error: (error: unknown) => {
          // Free meanwhile, or already waited for: the API says which.
          if (error instanceof ApiError && error.status === 409) {
            this.clearConflict();
            this.conflict.set(error.detail ?? error.title);
          }
        },
      });
  }

  /** "09:00-10:00" on the day asked for; "Tue 6 Oct, 09:00-10:00" on another. */
  protected suggestedTimeLabel(time: SuggestedTime): string {
    const start = new Date(time.start);
    const end = new Date(time.end);
    const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();
    const from = this.askedStart && sameDay(this.askedStart, start) ? 'HH:mm' : RANGE_FORMAT;
    const to = sameDay(start, end) ? 'HH:mm' : RANGE_FORMAT;
    return `${formatDate(start, from, this.locale)}-${formatDate(end, to, this.locale)}`;
  }

  protected submit(): void {
    // Re-run the time rules: the page may have been open since the start was picked.
    this.form.updateValueAndValidity();
    const rules = this.ruleProblems();
    if (
      this.form.invalid ||
      this.attendeesProblem() ||
      this.repeatError() ||
      rules.start ||
      rules.end
    ) {
      this.form.markAllAsTouched();
      return;
    }
    const recurrence = this.recurrence();
    if (recurrence) {
      this.submitSeries(recurrence);
      return;
    }

    const body = this.toRequest();
    const reservation = this.reservation();
    if (body.reminderMinutes !== null) {
      // Asked here because saving is a user action; asked at page load it would be blocked.
      this.reminderAlerts.requestPermissionIfUnasked();
    }
    const request = reservation
      ? this.reservationService.update(reservation.id, body)
      : this.reservationService.create(body);

    this.saving.set(true);
    this.formError.set(null);
    this.clearConflict();
    const before = new Set(reservation?.attendees.map((a) => a.id) ?? []);
    const invites = body.attendeeIds.some((id) => !before.has(id));
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        // Asked for, or asked again, when it waits for an Admin's approval (ADR-0049).
        const waits = saved.status === 'Pending';
        const done = reservation
          ? waits
            ? $localize`:@@reservationForm.savedAwaiting:Your changes to the ${saved.resourceName}:resource: reservation were saved. It waits for an Admin's approval.`
            : $localize`:@@reservationForm.saved:Your changes to the ${saved.resourceName}:resource: reservation were saved.`
          : waits
            ? $localize`:@@reservationForm.requested:${saved.resourceName}:resource: is asked for ${this.formatRange(saved)}:when:. An Admin approves it, and your inbox will say.`
            : $localize`:@@reservationForm.booked:${saved.resourceName}:resource: is booked for ${this.formatRange(saved)}:when:.`;
        this.notifications.success(
          invites
            ? `${done} ${$localize`:@@reservationForm.invitesSent:Your invitations were sent.`}`
            : done,
        );
        void this.router.navigateByUrl('/reservations/mine');
      },
      error: (error: unknown) => this.handleSaveError(error),
    });
  }

  /** Books every date of a repeating booking: the free ones, when the list showed some taken. */
  private submitSeries(recurrence: Recurrence): void {
    const preview = this.seriesPreview();
    const body: CreateSeriesRequest = {
      ...this.toRequest(),
      recurrence,
      skipConflicts: preview.state === 'ready' && preview.dates.some((date) => !date.free),
    };
    if (body.reminderMinutes !== null) {
      this.reminderAlerts.requestPermissionIfUnasked();
    }

    this.saving.set(true);
    this.formError.set(null);
    this.clearConflict();
    this.reservationService
      .createSeries(body)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (result) => {
          this.notifications.success(this.seriesBooked(result, body.attendeeIds?.length ?? 0));
          void this.router.navigateByUrl('/reservations/mine');
        },
        error: (error: unknown) => {
          if (
            error instanceof ApiError &&
            error.status === 409 &&
            error.code === SERIES_CONFLICTS
          ) {
            // A date was taken meanwhile: show the dates as they are now.
            this.conflict.set(error.detail ?? error.title);
            this.previewRun.update((run) => run + 1);
            return;
          }
          this.handleSaveError(error);
        },
      });
  }

  /** "Board Room is booked on 4 dates, every Monday until 2 Nov 2026, 10:00-11:00." */
  private seriesBooked(result: SeriesResult, invited: number): string {
    const first = result.booked[0];
    const name = first.resourceName;
    const count = result.booked.length;
    const rule = describeSeries(result.series, this.locale);
    const time = `${formatDate(first.startDateTime, 'HH:mm', this.locale)}-${formatDate(first.endDateTime, 'HH:mm', this.locale)}`;
    const lines = [
      $localize`:@@series.booked:${name}:resource: is booked on ${count}:count: dates, ${rule}:rule:, ${time}:time:.`,
    ];
    if (result.skipped.length > 0) {
      const skipped = result.skipped.length;
      lines.push(
        skipped === 1
          ? $localize`:@@series.skippedOne:One date wasn't free and was skipped.`
          : $localize`:@@series.skipped:${skipped}:count: dates weren't free and were skipped.`,
      );
    }
    if (invited > 0) {
      lines.push(
        $localize`:@@series.invitesSent:Each person you invited got one message for all the dates.`,
      );
    }
    return lines.join(' ');
  }

  /** "every Monday until 2 Nov 2026", for a booking that repeats. */
  protected describe(series: ReservationSeries): string {
    return describeSeries(series, this.locale);
  }

  /** "Mon", for the day buttons. */
  protected shortDay(day: WeekDay): string {
    return dayName(day, this.locale, true);
  }

  protected dayLabel(day: WeekDay): string {
    return dayName(day, this.locale);
  }

  /** Ticks or unticks a day of a weekly series; the day it starts on stays ticked. */
  protected toggleDay(day: WeekDay, on: boolean): void {
    const control = this.form.controls.repeatDays;
    const others = control.value.filter((existing) => existing !== day);
    control.setValue(on ? [...others, day] : others);
  }

  /** A first choice to repeat sets a last day four weeks on; changing it keeps the one chosen. */
  protected onRepeatChange(): void {
    const { repeat, until, startDateTime } = this.form.controls;
    const start = fromDateTimeLocal(startDateTime.value);
    if (repeat.value !== 'none' && !until.value && start) {
      const last = toDateParam(addDays(start, SERIES_DEFAULT_DAYS));
      until.setValue(last > this.maxUntil ? this.maxUntil : last);
    }
  }

  protected resourceError(): string | null {
    const control = this.form.controls.resourceId;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    return $localize`:@@reservationForm.chooseResource:Choose a resource.`;
  }

  protected startError(): string | null {
    const control = this.form.controls.startDateTime;
    if (!this.rangeTouched()) return null;
    if (control.hasError('required')) return RANGE_MESSAGES.chooseStart();
    if (control.hasError('server')) return control.getError('server') as string;
    if (this.form.hasError('startInPast')) return RANGE_MESSAGES.startInPast();
    if (this.form.hasError('startTooFarAhead')) {
      return $localize`:@@reservationForm.tooFarAhead:Bookings can start at most ${L.maxAdvanceDays}:days: days ahead.`;
    }
    return this.ruleProblems().start;
  }

  protected endError(): string | null {
    const control = this.form.controls.endDateTime;
    if (!this.rangeTouched()) return null;
    if (control.hasError('required')) return RANGE_MESSAGES.chooseEnd();
    if (control.hasError('server')) return control.getError('server') as string;
    if (this.form.hasError('endInPast')) {
      return $localize`:@@reservationForm.endPassed:The end time has already passed.`;
    }
    if (this.form.hasError('rangeOrder')) return RANGE_MESSAGES.endAfterStart();
    if (this.form.hasError('rangeTooLong')) {
      return $localize`:@@reservationForm.tooLong:A reservation can last at most ${L.maxDays}:days: days.`;
    }
    return this.ruleProblems().end;
  }

  /** "Ayşe Yılmaz (coming), Mehmet Demir (no answer yet)": who's invited, for the read-only view. */
  protected attendeeNames(reservation: Reservation): string {
    return reservation.attendees
      .map((a) => {
        const name = a.name ?? $localize`:@@common.unknownUser:Unknown user`;
        return `${name} (${attendeeResponseLabel(a.response)})`;
      })
      .join(', ');
  }

  /** An invitee answered from the read-only view: show the meeting as the API now has it. */
  protected onAnswered(meeting: Reservation): void {
    this.reservation.set(meeting);
  }

  /** Ticks or unticks someone on the list of who's invited. */
  protected toggleAttendee(id: string, invited: boolean): void {
    const control = this.form.controls.attendeeIds;
    const others = control.value.filter((existing) => existing !== id);
    control.setValue(invited ? [...others, id] : others);
    control.markAsTouched();
  }

  protected onAttendeeSearch(event: Event): void {
    this.attendeeSearch.set((event.target as HTMLInputElement).value);
  }

  /** What's wrong with the list of who's invited, as the API would say it. */
  protected attendeesError(): string | null {
    const control = this.form.controls.attendeeIds;
    if (control.hasError('server')) return control.getError('server') as string;
    return this.attendeesProblem();
  }

  protected attendeesHint(): string {
    const count = this.formValue().attendeeIds.length;
    return count > 0
      ? $localize`:@@reservationForm.attendeesHintCount:${count}:count: invited. Each gets a message in their SmartDesk inbox, and another if the meeting moves or is cancelled.`
      : $localize`:@@reservationForm.attendeesHint:Each person you invite gets a message in their SmartDesk inbox, and another if the meeting moves or is cancelled.`;
  }

  private attendeesProblem(): string | null {
    const count = this.formValue().attendeeIds.length;
    if (count > MAX_ATTENDEES) {
      return $localize`:@@reservationForm.tooManyAttendees:Invite at most ${MAX_ATTENDEES}:max: people.`;
    }
    const tooSmall = this.roomTooSmall();
    return tooSmall
      ? $localize`:@@reservationForm.roomTooSmall:${tooSmall.name}:resource: has room for ${tooSmall.capacity}:capacity:, and with you the meeting is ${tooSmall.people}:people:.`
      : null;
  }

  protected noteError(): string | null {
    const control = this.form.controls.note;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    return $localize`:@@field.maxLength:Use at most ${L.noteMaxLength}:max: characters.`;
  }

  private rangeTouched(): boolean {
    const { startDateTime, endDateTime } = this.form.controls;
    return startDateTime.touched || endDateTime.touched;
  }

  private prefillFromLink(): void {
    const linkStart = parseIso(this.start());
    const linkEnd = parseIso(this.end());
    const start = linkStart && linkEnd ? linkStart : nextHalfHour();
    const end = linkStart && linkEnd ? linkEnd : addMinutes(start, DEFAULT_DURATION_MIN);

    this.form.setValue({
      resourceId: this.resourceId() ?? '',
      startDateTime: toDateTimeLocal(start),
      endDateTime: toDateTimeLocal(end),
      note: '',
      reminderMinutes: DEFAULT_REMINDER_MINUTES,
      attendeeIds: [],
      repeat: 'none',
      repeatDays: [],
      until: '',
    });
    if (linkStart && linkEnd && this.form.invalid) {
      this.form.markAllAsTouched(); // e.g. an old link whose start is now in the past
    }
    this.keepDuration();
  }

  /** Cancels the reservation being changed, for its owner or an admin. */
  protected cancelReservation(): void {
    const reservation = this.reservation();
    if (!reservation) {
      return;
    }
    const someoneElse = this.bookedBy();
    const resource = reservation.resourceName;
    const when = this.formatRange(reservation);
    const question = someoneElse
      ? $localize`:@@reservationForm.confirmCancelOther:Cancel ${someoneElse}:owner:'s ${resource}:resource: reservation for ${when}:when:?`
      : $localize`:@@reservationForm.confirmCancel:Cancel your ${resource}:resource: reservation for ${when}:when:?`;
    if (!confirm(question)) {
      return;
    }

    this.cancelling.set(true);
    this.formError.set(null);
    this.clearConflict();
    this.reservationService
      .cancel(reservation.id)
      .pipe(finalize(() => this.cancelling.set(false)))
      .subscribe({
        next: (cancelled) => {
          const name = cancelled.resourceName;
          this.notifications.success(
            someoneElse
              ? $localize`:@@reservationForm.cancelledOther:${someoneElse}:owner:'s ${name}:resource: reservation for ${when}:when: was cancelled.`
              : $localize`:@@reservationForm.cancelled:Your ${name}:resource: reservation for ${when}:when: was cancelled.`,
          );
          // Back to the list it most likely came from: your own, or the admin overview.
          void this.router.navigateByUrl(someoneElse ? '/admin' : '/reservations/mine');
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 409) {
            this.load(reservation.id); // it ended meanwhile: show it read-only, with why
            return;
          }
          // 403/404/5xx already raised a toast; keep a note on the form as well.
          this.formError.set(
            $localize`:@@reservationForm.cancelFailed:The reservation could not be cancelled.`,
          );
        },
      });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.reservationService
      .getReservation(id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (reservation) => this.edit(reservation),
        error: () => this.loadFailed.set(true),
      });
  }

  private edit(reservation: Reservation): void {
    this.reservation.set(reservation);
    const start = new Date(reservation.startDateTime);
    const end = new Date(reservation.endDateTime);
    const now = Date.now();
    const user = this.authService.currentUser();

    // Same order of checks as the API, so the reason matches what a PUT would answer.
    let readOnlyReason: string | null = null;
    if (reservation.userId !== user?.id && !this.authService.isAdmin()) {
      const owner =
        reservation.userDisplayName ??
        $localize`:@@reservationForm.theBooker:the person who booked it`;
      readOnlyReason = reservation.attendees.some((a) => a.id === user?.id)
        ? $localize`:@@reservationForm.invitedYou:${owner}:owner: invited you to this meeting. Only they or an admin can change it.`
        : $localize`:@@reservationForm.onlyOwner:Only ${owner}:owner: or an admin can change this reservation.`;
    } else if (reservation.status === 'Cancelled') {
      readOnlyReason = $localize`:@@reservationForm.wasCancelled:This reservation was cancelled, so it can no longer be changed.`;
    } else if (end.getTime() <= now) {
      readOnlyReason = $localize`:@@reservationForm.hasEnded:This reservation has ended, so it can no longer be changed.`;
    }
    this.readOnlyReason.set(readOnlyReason);
    const mode: Mode = readOnlyReason
      ? 'readOnly'
      : start.getTime() <= now
        ? 'inProgress'
        : 'upcoming';
    this.mode.set(mode);

    const originalStart = toDateTimeLocal(start);
    this.form.setValidators(dateRangeValidator({ ...rangeRules(mode), originalStart }));
    this.form.setValue({
      resourceId: reservation.resourceId,
      startDateTime: originalStart,
      endDateTime: toDateTimeLocal(end),
      note: reservation.note ?? '',
      reminderMinutes: reservation.reminderMinutes,
      attendeeIds: reservation.attendees.map((a) => a.id),
      repeat: 'none',
      repeatDays: [],
      until: '',
    });
    if (mode === 'inProgress') {
      this.form.controls.resourceId.disable();
      this.form.controls.startDateTime.disable();
      // Too late for a reminder; the stored choice is sent back as it is.
      this.form.controls.reminderMinutes.disable();
    }
    this.keepDuration();
  }

  private keepDuration(): void {
    const { startDateTime, endDateTime } = this.form.controls;
    keepDurationWhenStartMoves(startDateTime, endDateTime, this.destroyRef);
  }

  private toRequest(): UpdateReservationRequest {
    const value = this.form.getRawValue();
    const original = this.reservation();
    return {
      resourceId: value.resourceId,
      startDateTime: toIso(value.startDateTime, original?.startDateTime),
      endDateTime: toIso(value.endDateTime, original?.endDateTime),
      note: value.note.trim() || null,
      reminderMinutes: value.reminderMinutes,
      // Always the whole list: a PUT without it would take everyone off.
      attendeeIds: value.attendeeIds,
    };
  }

  private clearConflict(): void {
    this.conflict.set(null);
    this.suggestions.set(null);
    this.mayWait.set(false);
  }

  private handleSaveError(error: unknown): void {
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.formError.set(unmatched.length ? unmatched.join(' ') : null);
      return;
    }
    if (error instanceof ApiError && error.status === 409) {
      this.conflict.set(error.detail ?? error.title);
      // A booking under way can only run longer: another time or resource is no answer.
      this.suggestions.set(this.mode() === 'inProgress' ? null : bookingSuggestions(error));
      // A new booking whose time is taken or blocked can wait for it instead (ADR-0043).
      this.mayWait.set(
        this.mode() === 'create' &&
          (error.code === RESERVATION_OVERLAPS || error.code === RESOURCE_BLOCKED),
      );
      this.askedStart = fromDateTimeLocal(this.form.getRawValue().startDateTime);
      return;
    }
    // Other failures already raised a toast; keep a note on the form as well.
    this.formError.set(
      $localize`:@@reservationForm.saveFailed:The reservation could not be saved.`,
    );
  }

  private formatRange(reservation: Pick<Reservation, 'startDateTime' | 'endDateTime'>): string {
    const start = formatDate(reservation.startDateTime, RANGE_FORMAT, this.locale);
    const end = formatDate(reservation.endDateTime, 'HH:mm', this.locale);
    const sameDay =
      new Date(reservation.startDateTime).toDateString() ===
      new Date(reservation.endDateTime).toDateString();
    return sameDay
      ? `${start}-${end}`
      : $localize`:@@common.rangeTo:${start}:start: to ${formatDate(reservation.endDateTime, RANGE_FORMAT, this.locale)}:end:`;
  }
}

/** Why the API wouldn't give the dates: a rule broken (400) or a resource out of use (409). */
function previewProblem(error: unknown): SeriesPreviewState {
  if (error instanceof ApiError && (error.status === 400 || error.status === 409)) {
    const message = Object.values(error.fieldErrors).flat()[0] ?? error.detail ?? error.title;
    return { state: 'invalid', message };
  }
  return { state: 'failed' };
}

/** The API's time rules for each mode. */
function rangeRules(mode: Mode): DateRangeOptions {
  const common = { startKey: 'startDateTime', endKey: 'endDateTime', maxDays: L.maxDays };
  return mode === 'inProgress'
    ? { ...common, endInFuture: true }
    : {
        ...common,
        futureOnly: mode !== 'readOnly',
        graceMinutes: L.pastStartGraceMinutes,
        maxAdvanceDays: mode === 'readOnly' ? undefined : L.maxAdvanceDays,
      };
}

/**
 * The UTC time to send for a datetime-local value. An unchanged value sends the original
 * timestamp back untouched, since datetime-local drops seconds and the API treats any
 * difference as a change of time (which has stricter rules once a reservation has started).
 */
function toIso(local: string, original?: string): string {
  if (original && toDateTimeLocal(new Date(original)) === local) {
    return original;
  }
  return fromDateTimeLocal(local)!.toISOString();
}

function toOption(resource: Resource): ResourceOption {
  return {
    id: resource.id,
    name: resource.name,
    resourceTypeId: resource.resourceTypeId,
    typeName: resource.resourceTypeName,
    location: resource.location,
    capacity: resource.capacity,
    features: resource.features,
    photoUpdatedAt: resource.photoUpdatedAt,
    checkInRequired: resource.checkInRequired,
  };
}

function groupByType(options: ResourceOption[]): { typeName: string; options: ResourceOption[] }[] {
  const groups = new Map<string, ResourceOption[]>();
  for (const option of options) {
    groups.set(option.typeName, [...(groups.get(option.typeName) ?? []), option]);
  }
  return [...groups]
    .map(([typeName, grouped]) => ({ typeName, options: grouped }))
    .sort((a, b) =>
      resourceTypeLabel(a.typeName).localeCompare(resourceTypeLabel(b.typeName), 'en'),
    );
}
