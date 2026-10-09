import { formatDate } from '@angular/common';
import { Component, DestroyRef, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Subject,
  catchError,
  filter,
  finalize,
  interval,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  RESOURCE_BLOCK_LIMITS,
  ResourceBlock,
  ResourceBlockQuery,
} from '../../core/models/resource-block.model';
import { CALENDAR_MAX_DAYS, CALENDAR_MAX_RANGE_MS } from '../../core/models/reservation.model';
import { Resource } from '../../core/models/resource.model';
import { NotificationService } from '../../core/services/notification.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import {
  addDays,
  addMinutes,
  floorToQuarterHour,
  formatDuration,
  fromDateTimeLocal,
  nextHalfHour,
  parseDateParam,
  startOfDay,
  toDateParam,
  toDateTimeLocal,
} from '../../shared/utils/date-time';
import { keepDurationWhenStartMoves } from '../../shared/utils/keep-duration';
import { applyServerErrors } from '../../shared/utils/server-errors';
import { RANGE_MESSAGES, dateRangeValidator } from '../../shared/validators/date-range.validator';
import { resourceStatusLabel, resourceTypeLabel } from '../../core/i18n/labels';

const L = RESOURCE_BLOCK_LIMITS;
const DEFAULT_DAYS = 28;
const DEFAULT_DURATION_MIN = 60;

interface Row {
  block: ResourceBlock;
  /** "Mon 5 Oct, 10:00-14:00", or with both dates when it spans days. */
  when: string;
  duration: string;
  /** "Meeting Room · 2nd Floor", once the resources have loaded. */
  meta: string | null;
  inProgress: boolean;
  ended: boolean;
}

/** A 409 from the API: another block or reservations overlap. Its detail is shown as-is. */
interface Conflict {
  message: string;
  /** Query parameters of the admin overview, when reservations stand in the way. */
  reservations: Record<string, string> | null;
}

interface ResourceGroup {
  typeName: string;
  resources: { id: string; label: string }[];
}

/**
 * Admin screen for blocked windows (`/admin/blocks`): take a resource out of booking for one
 * window, and see or remove the blocks in a date range. The range and resource filter live
 * in the query string. The API refuses a block that overlaps another block or a Confirmed
 * reservation; this screen shows why, and links to those reservations.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-resource-blocks',
  styleUrl: './resource-blocks.scss',
  templateUrl: './resource-blocks.html',
})
export class ResourceBlocks {
  protected readonly typeLabel = resourceTypeLabel;

  private readonly blockService = inject(ResourceBlockService);
  private readonly resourceService = inject(ResourceService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly locale = inject(LOCALE_ID);

  /** Query parameters: first and last day ("2026-10-05"), inclusive, and a resource id. */
  readonly from = input<string>();
  readonly to = input<string>();
  readonly resource = input<string>();

  protected readonly limits = L;

  private readonly resources = signal<Resource[]>([]);
  protected readonly resourcesFailed = signal(false);
  /** Every resource, whatever its status: a block can be planned before it's Active again. */
  protected readonly resourceGroups = computed(() => groupByType(this.resources()));
  private readonly resourcesById = computed(() => new Map(this.resources().map((r) => [r.id, r])));

  private readonly blocks = signal<ResourceBlock[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the block being removed, to disable its button meanwhile. */
  protected readonly removingId = signal<string | null>(null);
  private readonly retries = new Subject<void>();

  // Re-evaluated every minute for "in progress" and which blocks can still be removed.
  private readonly now = signal(Date.now());

  protected readonly range = computed(
    () => {
      const start = parseDateParam(this.from()) ?? startOfDay(new Date(this.now()));
      const last = parseDateParam(this.to()) ?? addDays(start, DEFAULT_DAYS - 1);
      return { start, last, end: addDays(last, 1) };
    },
    {
      // By value, so the per-minute clock doesn't count as a new range and refetch.
      equal: (a, b) =>
        a.start.getTime() === b.start.getTime() && a.last.getTime() === b.last.getTime(),
    },
  );
  protected readonly rangeError = computed(() => {
    const { start, last, end } = this.range();
    if (last < start) {
      return $localize`:@@admin.lastBeforeFirst:The last day is before the first day.`;
    }
    if (end.getTime() - start.getTime() > CALENDAR_MAX_RANGE_MS) {
      return $localize`:@@admin.tooLong:Show at most ${CALENDAR_MAX_DAYS}:days: days at a time.`;
    }
    return null;
  });

  protected readonly rows = computed((): Row[] => {
    const now = this.now();
    const resources = this.resourcesById();
    return [...this.blocks()]
      .sort((a, b) => a.startDateTime.localeCompare(b.startDateTime))
      .map((block) => {
        const start = new Date(block.startDateTime);
        const end = new Date(block.endDateTime);
        const resource = resources.get(block.resourceId);
        return {
          block,
          when: this.when(start, end),
          duration: formatDuration(start, end),
          meta: resource
            ? `${resourceTypeLabel(resource.resourceTypeName)} · ${resource.location}`
            : null,
          inProgress: start.getTime() <= now && end.getTime() > now,
          ended: end.getTime() <= now,
        };
      });
  });

  /** "3 blocks from Mon 5 Oct to Sun 11 Oct." */
  protected readonly summary = computed(() => {
    const n = this.rows().length;
    const from = formatDate(this.range().start, 'EEE d MMM', this.locale);
    const to = formatDate(this.range().last, 'EEE d MMM', this.locale);
    return n === 1
      ? $localize`:@@blocks.summaryOne:1 block from ${from}:from: to ${to}:to:.`
      : $localize`:@@blocks.summary:${n}:count: blocks from ${from}:from: to ${to}:to:.`;
  });

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      resourceId: ['', Validators.required],
      startDateTime: ['', Validators.required],
      endDateTime: ['', Validators.required],
      reason: [
        '',
        [Validators.required, Validators.pattern(/\S/), Validators.maxLength(L.reasonMaxLength)],
      ],
    },
    {
      validators: dateRangeValidator({
        startKey: 'startDateTime',
        endKey: 'endDateTime',
        maxDays: L.maxDays,
        futureOnly: true,
        graceMinutes: L.pastStartGraceMinutes,
      }),
    },
  );
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly conflict = signal<Conflict | null>(null);

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    {
      initialValue: this.form.getRawValue(),
    },
  );
  protected readonly selectedResource = computed(() =>
    this.resourcesById().get(this.formValue().resourceId),
  );
  /** Live duration hint under the end field. */
  protected readonly duration = computed(() => {
    const start = fromDateTimeLocal(this.formValue().startDateTime);
    const end = fromDateTimeLocal(this.formValue().endDateTime);
    return start && end && end > start ? formatDuration(start, end) : null;
  });
  protected readonly reasonLength = computed(() => this.formValue().reason.length);

  /** Earliest value offered by the start picker, on the quarter-hour grid its step expects. */
  protected readonly minStart = toDateTimeLocal(floorToQuarterHour());

  constructor() {
    this.resourceService
      .getResources()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (resources) => this.resources.set(resources),
        error: () => this.resourcesFailed.set(true),
      });

    const query = computed(
      (): ResourceBlockQuery | null =>
        this.rangeError()
          ? null
          : {
              from: this.range().start.toISOString(),
              to: this.range().end.toISOString(),
              resourceId: this.resource() || null,
            },
      { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
    );
    merge(toObservable(query), this.retries.pipe(map(() => query())))
      .pipe(
        filter((q): q is ResourceBlockQuery => q !== null),
        tap(() => this.loadFailed.set(false)),
        switchMap((q) => this.blockService.getBlocks(q).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((blocks) => {
        this.loading.set(false);
        if (blocks) {
          this.blocks.set(blocks);
        } else {
          this.loadFailed.set(true);
        }
      });

    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));

    this.resetForm();
    const { startDateTime, endDateTime } = this.form.controls;
    keepDurationWhenStartMoves(startDateTime, endDateTime, this.destroyRef);
  }

  /** "2026-10-05", for the date inputs. */
  protected dateValue(date: Date): string {
    return toDateParam(date);
  }

  protected setDay(param: 'from' | 'to', event: Event): void {
    this.navigate({ [param]: (event.target as HTMLInputElement).value || null });
  }

  protected selectResource(event: Event): void {
    this.navigate({ resource: (event.target as HTMLSelectElement).value || null });
  }

  protected retry(): void {
    this.loading.set(true);
    this.retries.next();
  }

  protected submit(): void {
    // Re-run the time rules: the page may have been open since the start was picked.
    this.form.updateValueAndValidity();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const start = fromDateTimeLocal(value.startDateTime)!;
    const end = fromDateTimeLocal(value.endDateTime)!;
    this.saving.set(true);
    this.formError.set(null);
    this.conflict.set(null);
    this.blockService
      .create({
        resourceId: value.resourceId,
        startDateTime: start.toISOString(),
        endDateTime: end.toISOString(),
        reason: value.reason.trim(),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (block) => {
          this.notifications.success(
            $localize`:@@blocks.created:${block.resourceName}:resource: is blocked for ${this.when(new Date(block.startDateTime), new Date(block.endDateTime))}:when:.`,
          );
          if (this.isInView(block)) {
            this.blocks.update((list) => [...list, block]);
          }
          this.resetForm();
        },
        error: (error: unknown) => this.handleSaveError(error, start, end, value.resourceId),
      });
  }

  protected remove(row: Row): void {
    const { block } = row;
    if (
      !confirm(
        $localize`:@@blocks.confirmRemove:Remove the block on ${block.resourceName}:resource: for ${row.when}:when:? It can then be booked in that window.`,
      )
    ) {
      return;
    }

    this.removingId.set(block.id);
    this.blockService
      .delete(block.id)
      .pipe(finalize(() => this.removingId.set(null)))
      .subscribe({
        next: () => {
          this.blocks.update((list) => list.filter((b) => b.id !== block.id));
          this.notifications.success(
            $localize`:@@blocks.removed:The block on ${block.resourceName}:resource: for ${row.when}:when: was removed.`,
          );
        },
        // 404: someone removed it already. 403/404/5xx already raised a toast; show the list
        // as it is now.
        error: () => this.retries.next(),
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
    return null;
  }

  protected endError(): string | null {
    const control = this.form.controls.endDateTime;
    if (!this.rangeTouched()) return null;
    if (control.hasError('required')) return RANGE_MESSAGES.chooseEnd();
    if (control.hasError('server')) return control.getError('server') as string;
    if (this.form.hasError('rangeOrder')) return RANGE_MESSAGES.endAfterStart();
    if (this.form.hasError('rangeTooLong')) {
      return $localize`:@@blocks.tooLong:A block can last at most ${L.maxDays}:days: days.`;
    }
    return null;
  }

  protected reasonError(): string | null {
    const control = this.form.controls.reason;
    if (!control.touched || !control.errors) return null;
    if (control.hasError('server')) return control.getError('server') as string;
    if (control.hasError('maxlength')) {
      return $localize`:@@field.maxLength:Use at most ${L.reasonMaxLength}:max: characters.`;
    }
    return $localize`:@@blocks.reasonRequired:Say why the resource is blocked.`;
  }

  private rangeTouched(): boolean {
    const { startDateTime, endDateTime } = this.form.controls;
    return startDateTime.touched || endDateTime.touched;
  }

  private resetForm(): void {
    const start = nextHalfHour();
    this.form.reset({
      resourceId: '',
      startDateTime: toDateTimeLocal(start),
      endDateTime: toDateTimeLocal(addMinutes(start, DEFAULT_DURATION_MIN)),
      reason: '',
    });
  }

  private handleSaveError(error: unknown, start: Date, end: Date, resourceId: string): void {
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.formError.set(unmatched.length ? unmatched.join(' ') : null);
      return;
    }
    if (error instanceof ApiError && error.status === 409) {
      const message = error.detail ?? error.title;
      // Two 409s, told apart by their code (the wording depends on the language): another
      // block, or Confirmed reservations the admin has to cancel or move first.
      const reservations =
        error.code === 'block-overlaps-reservations'
          ? this.reservationsParams(start, end, resourceId)
          : null;
      this.conflict.set({ message, reservations });
      return;
    }
    // Other failures already raised a toast; keep a note on the form as well.
    this.formError.set($localize`:@@blocks.saveFailed:The block could not be saved.`);
  }

  /**
   * The admin overview for the days the window touches, filtered to the resource's type, so
   * the clashing reservations can be cancelled or moved. A window longer than the overview's
   * six weeks opens on its first six.
   */
  private reservationsParams(start: Date, end: Date, resourceId: string): Record<string, string> {
    const first = startOfDay(start);
    const lastDay = startOfDay(new Date(end.getTime() - 1));
    const lastAllowed = addDays(first, CALENDAR_MAX_DAYS - 1);
    const last = lastDay < lastAllowed ? lastDay : lastAllowed;
    const type = this.resourcesById().get(resourceId)?.resourceTypeId;
    return { from: toDateParam(first), to: toDateParam(last), ...(type ? { type } : {}) };
  }

  private isInView(block: ResourceBlock): boolean {
    const { start, end } = this.range();
    const resource = this.resource();
    return (
      !this.rangeError() &&
      new Date(block.startDateTime) < end &&
      new Date(block.endDateTime) > start &&
      (!resource || block.resourceId === resource)
    );
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  private when(start: Date, end: Date): string {
    const sameDay = start.toDateString() === end.toDateString();
    const from = formatDate(start, 'EEE d MMM, HH:mm', this.locale);
    const to = formatDate(end, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
    return sameDay ? `${from}-${to}` : $localize`:@@common.rangeTo:${from}:start: to ${to}:end:`;
  }
}

function groupByType(resources: Resource[]): ResourceGroup[] {
  const groups = new Map<string, ResourceGroup['resources']>();
  for (const r of [...resources].sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
    const label = r.status === 'Active' ? r.name : `${r.name} (${resourceStatusLabel(r.status)})`;
    groups.set(r.resourceTypeName, [
      ...(groups.get(r.resourceTypeName) ?? []),
      { id: r.id, label },
    ]);
  }
  return [...groups]
    .map(([typeName, grouped]) => ({ typeName, resources: grouped }))
    .sort((a, b) =>
      resourceTypeLabel(a.typeName).localeCompare(resourceTypeLabel(b.typeName), 'en'),
    );
}
