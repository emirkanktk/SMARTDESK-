import { formatDate } from '@angular/common';
import {
  Component,
  DestroyRef,
  LOCALE_ID,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  Subject,
  catchError,
  distinctUntilChanged,
  finalize,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { issueCategoryLabel } from '../../../core/i18n/labels';
import { ApiError } from '../../../core/models/api-error.model';
import { IssueReport } from '../../../core/models/issue.model';
import { RESOURCE_BLOCK_LIMITS, ResourceBlock } from '../../../core/models/resource-block.model';
import { ReservationService } from '../../../core/services/reservation.service';
import { ResourceBlockService } from '../../../core/services/resource-block.service';
import {
  addDays,
  fromDateTimeLocal,
  startOfDay,
  toDateTimeLocal,
} from '../../../shared/utils/date-time';

/** How long a resource stays out of use while its problem is fixed. */
export type CloseFor = 'today' | 'tomorrow' | 'week' | 'until';

/** The longest it's closed from here: further ahead is the blocked windows screen's job. */
export const CLOSE_MAX_DAYS = 30;

/** When a closure starting at `now` ends, or null for a date that isn't chosen yet. */
export function closesAt(choice: CloseFor, now: Date, until: Date | null): Date | null {
  switch (choice) {
    case 'today':
      return addDays(startOfDay(now), 1);
    case 'tomorrow':
      return addDays(startOfDay(now), 2);
    case 'week':
      return addDays(startOfDay(now), 8);
    case 'until':
      return until;
  }
}

/**
 * Takes a resource out of use from a problem report (ADR-0037, ADR-0038): a blocked window
 * from now until a chosen time, with the problem as its reason. It says first how many
 * bookings that cancels; their people are told why. The calendar shows the window, and nobody
 * can book into it.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-take-out-of-use',
  styleUrl: './take-out-of-use.scss',
  templateUrl: './take-out-of-use.html',
})
export class TakeOutOfUse implements OnInit {
  readonly issue = input.required<IssueReport>();

  readonly closed = output<ResourceBlock>();
  readonly cancelled = output<void>();

  private readonly blocks = inject(ResourceBlockService);
  private readonly reservations = inject(ReservationService);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly limits = RESOURCE_BLOCK_LIMITS;
  protected readonly maxDays = CLOSE_MAX_DAYS;
  protected readonly form = inject(NonNullableFormBuilder).group({
    for: ['today' as CloseFor],
    until: [toDateTimeLocal(addDays(new Date(), 1))],
    reason: [
      '',
      [
        Validators.required,
        Validators.pattern(/\S/),
        Validators.maxLength(RESOURCE_BLOCK_LIMITS.reasonMaxLength),
      ],
    ],
  });

  protected readonly value = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    {
      initialValue: this.form.getRawValue(),
    },
  );

  /** The end the choice gives, from now; null while a chosen date is missing or not valid. */
  protected readonly end = computed(() => endOf(this.value()));
  protected readonly endText = computed(() => {
    const end = this.end();
    return end ? formatDate(end, 'EEE d MMM, HH:mm', this.locale) : null;
  });

  /** How many bookings closing it cancels: null while counting, or when they couldn't be. */
  protected readonly affected = signal<number | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly triedToSave = signal(false);
  /** Asks for the bookings to be counted again, the end unchanged: someone booked meanwhile. */
  private readonly recount = new Subject<void>();

  ngOnInit(): void {
    // The problem says why: "Air conditioning: too hot to sit in".
    const issue = this.issue();
    const reason = `${issueCategoryLabel(issue.category)}: ${issue.description}`;
    this.form.controls.reason.setValue(reason.slice(0, RESOURCE_BLOCK_LIMITS.reasonMaxLength));

    // Counted again whenever the end moves (not while the reason is typed), and when asked;
    // only the newest count is kept.
    const endTime = () => endOf(this.form.getRawValue())?.getTime() ?? null;
    merge(
      this.form.valueChanges.pipe(startWith(null), map(endTime), distinctUntilChanged()),
      this.recount.pipe(map(endTime)),
    )
      .pipe(
        switchMap((time) => {
          this.affected.set(null);
          if (time === null) {
            return of(null);
          }
          const end = new Date(time);
          const now = new Date();
          return this.reservations
            .getCalendar({
              from: now.toISOString(),
              to: end.toISOString(),
              resourceId: this.issue().resourceId,
            })
            .pipe(
              map(
                (bookings) =>
                  bookings.filter(
                    (r) =>
                      r.status === 'Confirmed' &&
                      new Date(r.startDateTime) < end &&
                      now < new Date(r.endDateTime),
                  ).length,
              ),
              catchError(() => of(null)),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((count) => this.affected.set(count));
  }

  protected close(): void {
    this.triedToSave.set(true);
    const end = this.end();
    if (this.form.invalid || !end || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.blocks
      .create({
        resourceId: this.issue().resourceId,
        startDateTime: new Date().toISOString(),
        endDateTime: end.toISOString(),
        reason: this.form.controls.reason.value.trim(),
        // Only what was counted and shown: a booking made since then is a 409 to look at.
        cancelReservations: (this.affected() ?? 0) > 0,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (block) => this.closed.emit(block),
        error: (error: unknown) => {
          this.error.set(
            error instanceof ApiError && error.status !== 0 && error.status < 500
              ? (error.detail ?? error.title)
              : $localize`:@@takeOutOfUse.failed:It could not be closed. Check your connection and try again.`,
          );
          // Someone booked meanwhile: count again, so the next try says how many go.
          if (error instanceof ApiError && error.code === 'block-overlaps-reservations') {
            this.recount.next();
          }
        },
      });
  }
}

/** The end a choice gives from now, if it's after now and at most CLOSE_MAX_DAYS ahead. */
function endOf(value: { for: CloseFor; until: string }): Date | null {
  const now = new Date();
  const end = closesAt(value.for, now, fromDateTimeLocal(value.until));
  return end && end > now && end <= addDays(now, CLOSE_MAX_DAYS) ? end : null;
}
