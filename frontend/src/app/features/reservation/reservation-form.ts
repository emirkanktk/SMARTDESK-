import { DatePipe, formatDate } from '@angular/common';
import {
  Component,
  DestroyRef,
  LOCALE_ID,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  AttendeeResponse,
  CHECK_IN,
  DEFAULT_REMINDER_MINUTES,
  Invitee,
  MAX_ATTENDEES,
  REMINDER_OPTIONS,
  RESERVATION_LIMITS,
  Reservation,
  UpdateReservationRequest,
} from '../../core/models/reservation.model';
import { Resource } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReminderAlertsService } from '../../core/services/reminder-alerts.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceService } from '../../core/services/resource.service';
import { MeetingAnswer } from '../../shared/components/meeting-answer/meeting-answer';
import { ResourcePhoto } from '../../shared/components/resource-photo/resource-photo';
import {
  addMinutes,
  floorToQuarterHour,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseIso,
  toDateTimeLocal,
} from '../../shared/utils/date-time';
import { keepDurationWhenStartMoves } from '../../shared/utils/keep-duration';
import { applyServerErrors } from '../../shared/utils/server-errors';
import {
  DateRangeOptions,
  RANGE_MESSAGES,
  dateRangeValidator,
} from '../../shared/validators/date-range.validator';
import { attendeeResponseLabel, reservationStatusLabel } from '../../core/i18n/labels';

const L = RESERVATION_LIMITS;
const DEFAULT_DURATION_MIN = 60;
const RANGE_FORMAT = 'EEE d MMM, HH:mm';
/** From this many people on, the list of who to invite gets a search box. */
export const ATTENDEE_SEARCH_FROM = 8;

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
 * and Admins can invite people to it, who get a message in their inbox (ADR-0032). Anyone else
 * who opens a booking, e.g. someone invited to it, sees it read-only.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe, MeetingAnswer, ResourcePhoto],
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
    },
    { validators: dateRangeValidator(rangeRules('create')) },
  );

  protected readonly mode = signal<Mode>('create');
  /** The reservation being changed; null when booking. */
  protected readonly reservation = signal<Reservation | null>(null);
  protected readonly readOnlyReason = signal<string | null>(null);
  protected readonly statusLabel = reservationStatusLabel;
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

  // getRawValue: a locked (disabled) start or resource still counts for the hints.
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );
  protected readonly selectedResource = computed(() =>
    this.resourceOptions().find((r) => r.id === this.formValue().resourceId),
  );
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
    const term = this.attendeeSearch().trim().toLowerCase();
    return term
      ? this.people().filter(
          (p) => p.name.toLowerCase().includes(term) || p.email?.toLowerCase().includes(term),
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
      reservation.status === 'Confirmed' &&
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

  ngOnInit(): void {
    if (this.authService.isManager() || this.authService.isAdmin()) {
      this.reservationService.getInvitees().subscribe({
        next: (invitees) => this.invitees.set(invitees),
        error: () => this.inviteesFailed.set(true),
      });
    }

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

  protected submit(): void {
    // Re-run the time rules: the page may have been open since the start was picked.
    this.form.updateValueAndValidity();
    if (this.form.invalid || this.attendeesProblem()) {
      this.form.markAllAsTouched();
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
    this.conflict.set(null);
    const before = new Set(reservation?.attendees.map((a) => a.id) ?? []);
    const invites = body.attendeeIds.some((id) => !before.has(id));
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        const done = reservation
          ? $localize`:@@reservationForm.saved:Your changes to the ${saved.resourceName}:resource: reservation were saved.`
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
    return null;
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
    return null;
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
    this.conflict.set(null);
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

  private handleSaveError(error: unknown): void {
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.formError.set(unmatched.length ? unmatched.join(' ') : null);
      return;
    }
    if (error instanceof ApiError && error.status === 409) {
      this.conflict.set(error.detail ?? error.title);
      return;
    }
    // Other failures already raised a toast; keep a note on the form as well.
    this.formError.set(
      $localize`:@@reservationForm.saveFailed:The reservation could not be saved.`,
    );
  }

  private formatRange(reservation: Reservation): string {
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
    .sort((a, b) => a.typeName.localeCompare(b.typeName, 'en'));
}
