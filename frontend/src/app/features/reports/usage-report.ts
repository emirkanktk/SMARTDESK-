import { formatNumber, formatPercent } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, filter, map, merge, of, switchMap, tap } from 'rxjs';
import {
  REPORT_MAX_DAYS,
  UsageReport as UsageReportData,
  UsageReportQuery,
} from '../../core/models/report.model';
import { ResourceType } from '../../core/models/resource.model';
import { browserTimeZone } from '../../core/services/dashboard.service';
import { ReportService } from '../../core/services/report.service';
import { ResourceService } from '../../core/services/resource.service';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { addDays, parseDateParam, startOfDay, toDateParam } from '../../shared/utils/date-time';

const DEFAULT_DAYS = 30;
const DAY_MS = 24 * 60 * 60 * 1000;

type Preset = 'last7' | 'last30' | 'thisMonth' | 'lastMonth';

/** Calendar days from `a` to `b`, whatever daylight saving does to the hours between them. */
function daysBetween(a: Date, b: Date): number {
  const day = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
  return Math.round((day(b) - day(a)) / DAY_MS);
}

/**
 * Admin usage report: how much each resource, type and person booked over a range of whole
 * days, and how full the office day was (`GET /api/reports/usage`, ADR-0017). The range and
 * type live in the query string. A snapshot: it doesn't follow live events, the Refresh
 * button reloads it.
 */
@Component({
  imports: [RouterLink, StatusBadge],
  selector: 'app-usage-report',
  styleUrl: './usage-report.scss',
  templateUrl: './usage-report.html',
})
export class UsageReport {
  private readonly reportService = inject(ReportService);
  private readonly resourceService = inject(ResourceService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);
  private readonly timeZone = browserTimeZone();

  /** Query parameters: first and last day ("2026-10-05"), both included, and a type id. */
  readonly from = input<string>();
  readonly to = input<string>();
  readonly type = input<string>();

  protected readonly maxDays = REPORT_MAX_DAYS;
  protected readonly types = signal<ResourceType[]>([]);
  protected readonly report = signal<UsageReportData | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  private readonly reloads = new Subject<void>();

  /** Defaults to the last 30 days, today included. */
  protected readonly range = computed(
    () => {
      const last = parseDateParam(this.to()) ?? startOfDay(new Date());
      const first = parseDateParam(this.from()) ?? addDays(last, -(DEFAULT_DAYS - 1));
      return { first, last };
    },
    {
      equal: (a, b) =>
        a.first.getTime() === b.first.getTime() && a.last.getTime() === b.last.getTime(),
    },
  );

  protected readonly rangeError = computed(() => {
    const { first, last } = this.range();
    const days = daysBetween(first, last) + 1;
    if (days < 1) {
      return $localize`:@@admin.lastBeforeFirst:The last day is before the first day.`;
    }
    if (days > REPORT_MAX_DAYS) {
      return $localize`:@@admin.tooLong:Show at most ${REPORT_MAX_DAYS}:days: days at a time.`;
    }
    return null;
  });

  constructor() {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });

    const query = computed(
      (): UsageReportQuery | null =>
        this.rangeError()
          ? null
          : {
              from: toDateParam(this.range().first),
              to: toDateParam(this.range().last),
              timeZone: this.timeZone,
              resourceTypeId: this.type() || null,
            },
      { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
    );

    merge(toObservable(query), this.reloads.pipe(map(() => query())))
      .pipe(
        filter((q): q is UsageReportQuery => q !== null),
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        // switchMap: when the range changes quickly, only the newest answer is shown.
        switchMap((q) => this.reportService.getUsage(q).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(),
      )
      .subscribe((report) => {
        this.loading.set(false);
        if (report) {
          this.report.set(report);
        } else {
          this.loadFailed.set(true);
        }
      });
  }

  protected dateValue(date: Date): string {
    return toDateParam(date);
  }

  protected setDay(param: 'from' | 'to', event: Event): void {
    this.navigate({ [param]: (event.target as HTMLInputElement).value || null });
  }

  protected selectType(event: Event): void {
    this.navigate({ type: (event.target as HTMLSelectElement).value || null });
  }

  protected applyPreset(preset: Preset): void {
    const today = startOfDay(new Date());
    const ranges: Record<Preset, [Date, Date]> = {
      last7: [addDays(today, -6), today],
      last30: [addDays(today, -(DEFAULT_DAYS - 1)), today],
      thisMonth: [new Date(today.getFullYear(), today.getMonth(), 1), today],
      // Day 0 of this month is the last day of the previous one.
      lastMonth: [
        new Date(today.getFullYear(), today.getMonth() - 1, 1),
        new Date(today.getFullYear(), today.getMonth(), 0),
      ],
    };
    const [first, last] = ranges[preset];
    this.navigate({ from: toDateParam(first), to: toDateParam(last) });
  }

  protected reload(): void {
    this.reloads.next();
  }

  protected percent(fraction: number): string {
    return formatPercent(fraction, this.locale, '1.0-1');
  }

  protected hours(value: number): string {
    const hours = formatNumber(value, this.locale, '1.0-1');
    return $localize`:@@duration.hours:${hours}:hours: h`;
  }

  /** Width of a utilization bar: two decimals, and capped so a rounding quirk can't overflow it. */
  protected barWidth(fraction: number): string {
    const percent = Math.round(fraction * 10_000) / 100;
    return `${Math.min(100, Math.max(0, percent))}%`;
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }
}
