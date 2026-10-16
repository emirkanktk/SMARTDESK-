import { Component, LOCALE_ID, OnInit, WritableSignal, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { finalize } from 'rxjs';
import { resourceTypeLabel } from '../../core/i18n/labels';
import { ApiError } from '../../core/models/api-error.model';
import {
  BookingRules,
  RULE_STEP_MINUTES,
  ResourceTypeRules,
  RuleBounds,
} from '../../core/models/booking-rules.model';
import { WEEK_DAYS, WeekDay } from '../../core/models/reservation.model';
import { BookingRulesService } from '../../core/services/booking-rules.service';
import { NotificationService } from '../../core/services/notification.service';
import { minutesOf } from '../../shared/utils/booking-rules';
import { dayName } from '../../shared/utils/recurrence';
import { applyServerErrors } from '../../shared/utils/server-errors';

const HOUR_MINUTES = 60;
const DAY_MINUTES = 24 * HOUR_MINUTES;

type KindForm = FormGroup<{
  /** The longest booking, in hours: 1.5 is an hour and a half. Empty: no rule of its own. */
  maxHours: FormControl<number | null>;
  daysAhead: FormControl<number | null>;
  perPerson: FormControl<number | null>;
  officeHoursOnly: FormControl<boolean>;
  /** A booking waits for an Admin's approval (ADR-0049). */
  requiresApproval: FormControl<boolean>;
}>;

/** One kind of resource on the page, with a form of its own. */
interface KindRow {
  rules: ResourceTypeRules;
  form: KindForm;
  saving: WritableSignal<boolean>;
  error: WritableSignal<string | null>;
}

/**
 * The rules bookings keep to (`/admin/rules`, ADR-0046): when the office is open, and for each
 * kind of resource how long a booking may last, how far ahead it may start, how many one person
 * may have, and whether it keeps to office hours. Every way of booking keeps to them; bookings
 * already made stay as they are.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-booking-rules',
  styleUrl: './booking-rules.scss',
  templateUrl: './booking-rules.html',
})
export class BookingRulesAdmin implements OnInit {
  private readonly service = inject(BookingRulesService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly typeLabel = resourceTypeLabel;
  protected readonly weekDays = WEEK_DAYS;

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly bounds = signal<RuleBounds | null>(null);
  protected readonly kinds = signal<KindRow[]>([]);
  /** The IANA zones the browser knows, and the office's own even if it doesn't. */
  protected readonly timeZones = signal<string[]>([]);

  protected readonly officeForm = this.fb.group(
    {
      timeZone: ['', Validators.required],
      opensAt: ['', [Validators.required, onQuarterHour]],
      closesAt: ['', [Validators.required, onQuarterHour]],
      workdays: this.fb.control<WeekDay[]>([], atLeastOneDay),
    },
    { validators: closesAfterOpens },
  );
  protected readonly savingOffice = signal(false);
  protected readonly officeError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .get()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (rules) => this.show(rules),
        error: () => this.loadFailed.set(true),
      });
  }

  protected dayLabel(day: WeekDay): string {
    return dayName(day, this.locale);
  }

  protected shortDay(day: WeekDay): string {
    return dayName(day, this.locale, true);
  }

  /** Ticks or unticks a workday. */
  protected toggleDay(day: WeekDay, on: boolean): void {
    const control = this.officeForm.controls.workdays;
    const others = control.value.filter((existing) => existing !== day);
    control.setValue(on ? [...others, day] : others);
    control.markAsTouched();
  }

  protected saveOffice(): void {
    if (this.officeForm.invalid) {
      this.officeForm.markAllAsTouched();
      return;
    }

    const value = this.officeForm.getRawValue();
    this.savingOffice.set(true);
    this.officeError.set(null);
    this.service
      .setOfficeHours({
        timeZone: value.timeZone,
        opensAt: value.opensAt,
        closesAt: value.closesAt,
        workdays: WEEK_DAYS.filter((day) => value.workdays.includes(day)),
      })
      .pipe(finalize(() => this.savingOffice.set(false)))
      .subscribe({
        next: (saved) => {
          this.officeForm.markAsPristine();
          const hours = saved.description;
          this.notifications.success(
            $localize`:@@rules.officeSaved:Office hours saved: ${hours}:hours:.`,
          );
        },
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 400) {
            const unmatched = applyServerErrors(this.officeForm, error);
            this.officeError.set(unmatched.length ? unmatched.join(' ') : null);
            return;
          }
          this.officeError.set(
            $localize`:@@rules.officeFailed:The office hours could not be saved.`,
          );
        },
      });
  }

  protected saveKind(row: KindRow): void {
    if (row.form.invalid) {
      row.form.markAllAsTouched();
      return;
    }

    const value = row.form.getRawValue();
    row.saving.set(true);
    row.error.set(null);
    this.service
      .setTypeRules(row.rules.resourceTypeId, {
        maxDurationMinutes:
          value.maxHours === null ? null : Math.round(value.maxHours * HOUR_MINUTES),
        maxDaysAhead: value.daysAhead,
        maxActivePerUser: value.perPerson,
        officeHoursOnly: value.officeHoursOnly,
        requiresApproval: value.requiresApproval,
      })
      .pipe(finalize(() => row.saving.set(false)))
      .subscribe({
        next: (saved) => {
          row.rules = saved;
          row.form.markAsPristine();
          const kind = resourceTypeLabel(saved.name);
          this.notifications.success($localize`:@@rules.kindSaved:Rules for ${kind}:kind: saved.`);
        },
        error: (error: unknown) => {
          const messages =
            error instanceof ApiError && error.status === 400
              ? Object.values(error.fieldErrors).flat()
              : [];
          row.error.set(
            messages.length > 0
              ? messages.join(' ')
              : $localize`:@@rules.kindFailed:The rules could not be saved.`,
          );
        },
      });
  }

  protected opensError(): string | null {
    return this.timeError(this.officeForm.controls.opensAt);
  }

  protected closesError(): string | null {
    const control = this.officeForm.controls.closesAt;
    const own = this.timeError(control);
    if (own || !control.touched) return own;
    return this.officeForm.hasError('closesBeforeOpens')
      ? $localize`:@@rules.closesBeforeOpens:The office has to close after it opens, on the same day.`
      : null;
  }

  protected zoneError(): string | null {
    const control = this.officeForm.controls.timeZone;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    return $localize`:@@rules.chooseZone:Choose the office's time zone.`;
  }

  protected workdaysError(): string | null {
    const control = this.officeForm.controls.workdays;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    return $localize`:@@rules.noWorkdays:Pick at least one workday.`;
  }

  protected hoursError(row: KindRow): string | null {
    const control = row.form.controls.maxHours;
    if (!control.touched || !control.errors) return null;
    const max = (this.bounds()?.maxDurationMinutes ?? 0) / HOUR_MINUTES;
    return $localize`:@@rules.hoursRange:From 0.25 to ${max}:max: hours, in quarter hours.`;
  }

  protected daysAheadError(row: KindRow): string | null {
    const control = row.form.controls.daysAhead;
    if (!control.touched || !control.errors) return null;
    const max = this.bounds()?.maxDaysAhead ?? 0;
    return $localize`:@@rules.daysAheadRange:A whole number from 0 (today only) to ${max}:max:.`;
  }

  protected perPersonError(row: KindRow): string | null {
    const control = row.form.controls.perPerson;
    if (!control.touched || !control.errors) return null;
    const max = this.bounds()?.maxActivePerUser ?? 0;
    return $localize`:@@rules.perPersonRange:A whole number from 1 to ${max}:max:.`;
  }

  /** "31 days": the longest any booking may last. */
  protected generalDays(bounds: RuleBounds): number {
    return Math.floor(bounds.maxDurationMinutes / DAY_MINUTES);
  }

  private timeError(control: AbstractControl<string>): string | null {
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    if (control.hasError('required')) {
      return $localize`:@@rules.chooseTime:Choose a time.`;
    }
    return $localize`:@@rules.quarterHour:Use a time on the quarter hour, such as 08:00, 08:15 or 08:30.`;
  }

  private show(rules: BookingRules): void {
    const office = rules.officeHours;
    this.officeForm.reset({
      timeZone: office.timeZone,
      opensAt: office.opensAt.slice(0, 5),
      closesAt: office.closesAt.slice(0, 5),
      workdays: [...office.workdays],
    });
    const zones = Intl.supportedValuesOf('timeZone');
    this.timeZones.set(zones.includes(office.timeZone) ? zones : [office.timeZone, ...zones]);
    this.bounds.set(rules.bounds);
    this.kinds.set(
      [...rules.types]
        .sort((a, b) =>
          resourceTypeLabel(a.name).localeCompare(resourceTypeLabel(b.name), this.locale),
        )
        .map((kind) => this.row(kind, rules.bounds)),
    );
  }

  private row(rules: ResourceTypeRules, bounds: RuleBounds): KindRow {
    const minutes = rules.maxDurationMinutes;
    return {
      rules,
      form: this.fb.group({
        maxHours: this.fb.control<number | null>(minutes === null ? null : minutes / HOUR_MINUTES, [
          Validators.min(RULE_STEP_MINUTES / HOUR_MINUTES),
          Validators.max(bounds.maxDurationMinutes / HOUR_MINUTES),
          inQuarterHours,
        ]),
        daysAhead: this.fb.control<number | null>(rules.maxDaysAhead, [
          Validators.min(0),
          Validators.max(bounds.maxDaysAhead),
          wholeNumber,
        ]),
        perPerson: this.fb.control<number | null>(rules.maxActivePerUser, [
          Validators.min(1),
          Validators.max(bounds.maxActivePerUser),
          wholeNumber,
        ]),
        officeHoursOnly: this.fb.control(rules.officeHoursOnly),
        requiresApproval: this.fb.control(rules.requiresApproval),
      }),
      saving: signal(false),
      error: signal(null),
    };
  }
}

/** "08:15" yes, "08:10" no: office hours go in the booking form's quarter hours. */
function onQuarterHour(control: AbstractControl<string>): ValidationErrors | null {
  return !control.value || minutesOf(control.value) % RULE_STEP_MINUTES === 0
    ? null
    : { quarterHour: true };
}

/** 1.25 hours yes, 1.1 no. */
function inQuarterHours(control: AbstractControl<number | null>): ValidationErrors | null {
  const hours = control.value;
  return hours === null || Number.isInteger(hours * (HOUR_MINUTES / RULE_STEP_MINUTES))
    ? null
    : { quarterHour: true };
}

function wholeNumber(control: AbstractControl<number | null>): ValidationErrors | null {
  return control.value === null || Number.isInteger(control.value) ? null : { wholeNumber: true };
}

function atLeastOneDay(control: AbstractControl<WeekDay[]>): ValidationErrors | null {
  return control.value.length > 0 ? null : { noWorkdays: true };
}

function closesAfterOpens(group: AbstractControl): ValidationErrors | null {
  const { opensAt, closesAt } = group.value as { opensAt?: string; closesAt?: string };
  return opensAt && closesAt && minutesOf(closesAt) <= minutesOf(opensAt)
    ? { closesBeforeOpens: true }
    : null;
}
