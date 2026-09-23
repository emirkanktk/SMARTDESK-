import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  AVAILABILITY_MAX_DAYS,
  AvailabilityQuery,
  Resource,
  ResourceType,
} from '../../core/models/resource.model';
import { ResourceService } from '../../core/services/resource.service';
import {
  addMinutes,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseIso,
  toDateTimeLocal,
} from '../../shared/utils/date-time';
import { applyServerErrors } from '../../shared/utils/server-errors';
import { dateRangeValidator } from '../../shared/validators/date-range.validator';
import { integerValidator } from '../../shared/validators/integer.validator';

const DEFAULT_DURATION_MIN = 60;

interface SearchResult {
  query: AvailabilityQuery;
  resources: Resource[];
}

/** Find resources that are free for a time range, then go on to book one. */
@Component({
  imports: [ReactiveFormsModule, RouterLink, DatePipe],
  selector: 'app-availability',
  styleUrl: './availability.scss',
  templateUrl: './availability.html',
})
export class Availability implements OnInit {
  private readonly resourceService = inject(ResourceService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  // The last search lives in the query string (UTC ISO times), so it survives a reload and
  // coming back from the booking form.
  readonly start = input<string>();
  readonly end = input<string>();
  readonly type = input<string>();
  readonly capacity = input<string>();

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      startDateTime: ['', Validators.required],
      endDateTime: ['', Validators.required],
      resourceTypeId: [''],
      minCapacity: [null as number | null, [Validators.min(1), integerValidator]],
    },
    {
      validators: dateRangeValidator({
        startKey: 'startDateTime',
        endKey: 'endDateTime',
        maxDays: AVAILABILITY_MAX_DAYS,
        futureOnly: true,
      }),
    },
  );

  protected readonly types = signal<ResourceType[]>([]);
  protected readonly searching = signal(false);
  protected readonly searchError = signal<string | null>(null);
  /** Null until the first search completes. */
  protected readonly result = signal<SearchResult | null>(null);

  protected readonly searchedStart = computed(() => parseIso(this.result()?.query.startDateTime));
  protected readonly searchedEnd = computed(() => parseIso(this.result()?.query.endDateTime));
  protected readonly searchedDuration = computed(() => {
    const start = this.searchedStart();
    const end = this.searchedEnd();
    return start && end ? formatDuration(start, end) : '';
  });

  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  /** Live duration hint under the end field. */
  protected readonly duration = computed(() => {
    const start = fromDateTimeLocal(this.formValue().startDateTime);
    const end = fromDateTimeLocal(this.formValue().endDateTime);
    return start && end && end > start ? formatDuration(start, end) : null;
  });

  /** Earliest value offered by the start picker. */
  protected readonly minStart = toDateTimeLocal(new Date());

  private readonly searches = new Subject<AvailabilityQuery>();

  constructor() {
    this.searches
      .pipe(
        tap(() => {
          this.searching.set(true);
          this.searchError.set(null);
        }),
        // switchMap drops the answer to a search the user has already replaced.
        switchMap((query) =>
          this.resourceService.getAvailable(query).pipe(
            map((resources): SearchResult => ({ query, resources })),
            catchError((error: unknown) => {
              this.handleSearchError(error);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.searching.set(false);
        if (result) {
          this.result.set(result);
        }
      });
  }

  ngOnInit(): void {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });

    const start = parseIso(this.start());
    const end = parseIso(this.end());
    if (start && end) {
      this.fill(start, end, this.type() ?? '', Number(this.capacity()) || null);
      if (this.form.valid) {
        this.searches.next(this.toQuery());
      } else {
        this.form.markAllAsTouched(); // e.g. an old link whose start is now in the past
      }
    } else {
      const defaultStart = nextHalfHour();
      this.fill(defaultStart, addMinutes(defaultStart, DEFAULT_DURATION_MIN), '', null);
    }

    this.keepDurationWhenStartMoves();
  }

  protected search(): void {
    // Re-run "start in the past": the page may have been open since the start was picked.
    this.form.updateValueAndValidity();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const query = this.toQuery();
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        start: query.startDateTime,
        end: query.endDateTime,
        type: query.resourceTypeId || null,
        capacity: query.minCapacity || null,
      },
    });
    this.searches.next(query);
  }

  /** Query parameters that pre-fill the booking form with this resource and time. */
  protected bookingParams(resource: Resource): Record<string, string> {
    const query = this.result()!.query;
    return { resourceId: resource.id, start: query.startDateTime, end: query.endDateTime };
  }

  protected startError(): string | null {
    const control = this.form.controls.startDateTime;
    if (!this.rangeTouched()) return null;
    if (control.hasError('required')) return 'Choose a start date and time.';
    if (control.hasError('server')) return control.getError('server') as string;
    if (this.form.hasError('startInPast')) return 'The start time is in the past.';
    return null;
  }

  protected endError(): string | null {
    const control = this.form.controls.endDateTime;
    if (!this.rangeTouched()) return null;
    if (control.hasError('required')) return 'Choose an end date and time.';
    if (control.hasError('server')) return control.getError('server') as string;
    if (this.form.hasError('rangeOrder')) return 'The end must be after the start.';
    if (this.form.hasError('rangeTooLong')) {
      return `Search at most ${AVAILABILITY_MAX_DAYS} days at a time.`;
    }
    return null;
  }

  protected capacityError(): string | null {
    const control = this.form.controls.minCapacity;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    return 'Enter a whole number of at least 1.';
  }

  private rangeTouched(): boolean {
    const { startDateTime, endDateTime } = this.form.controls;
    return startDateTime.touched || endDateTime.touched;
  }

  private fill(start: Date, end: Date, resourceTypeId: string, minCapacity: number | null): void {
    this.form.setValue({
      startDateTime: toDateTimeLocal(start),
      endDateTime: toDateTimeLocal(end),
      resourceTypeId,
      minCapacity,
    });
  }

  private toQuery(): AvailabilityQuery {
    const value = this.form.getRawValue();
    return {
      startDateTime: fromDateTimeLocal(value.startDateTime)!.toISOString(),
      endDateTime: fromDateTimeLocal(value.endDateTime)!.toISOString(),
      resourceTypeId: value.resourceTypeId || null,
      minCapacity: value.minCapacity ? Number(value.minCapacity) : null,
    };
  }

  // Moving the start moves the end with it, keeping the chosen duration - the way calendar
  // apps behave, and it saves re-picking the end every time.
  private keepDurationWhenStartMoves(): void {
    const { startDateTime, endDateTime } = this.form.controls;
    let previousStart = fromDateTimeLocal(startDateTime.value);

    startDateTime.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      const start = fromDateTimeLocal(value);
      const end = fromDateTimeLocal(endDateTime.value);
      if (start && previousStart && end && end > previousStart) {
        endDateTime.setValue(
          toDateTimeLocal(new Date(end.getTime() + start.getTime() - previousStart.getTime())),
        );
      }
      previousStart = start;
    });
  }

  private handleSearchError(error: unknown): void {
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.searchError.set(unmatched.length ? unmatched.join(' ') : null);
      return;
    }
    // 0 / 5xx already raised a toast; say it here too, next to where the results would be.
    this.searchError.set('The search could not be completed. Please try again.');
  }
}
