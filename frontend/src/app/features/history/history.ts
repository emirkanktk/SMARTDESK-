import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, finalize, map, of, startWith, switchMap, tap } from 'rxjs';
import { AUDIT_AREAS, AuditArea, AuditEntry, AuditFilter } from '../../core/models/audit.model';
import { UserAccount } from '../../core/models/account.model';
import { AccountService } from '../../core/services/account.service';
import { AuditService } from '../../core/services/audit.service';
import { ResourceService } from '../../core/services/resource.service';
import { actionLabel, actorLabel, changeLines, entityLabel, subject } from './audit-text';

/** How each area of the history reads in its filter. */
const AREA_LABELS: Record<AuditArea, () => string> = {
  bookings: () => $localize`:@@history.area.bookings:Bookings`,
  resources: () => $localize`:@@history.area.resources:Resources`,
  rules: () => $localize`:@@history.area.rules:Booking rules`,
  problems: () => $localize`:@@history.area.problems:Problems`,
  accounts: () => $localize`:@@history.area.accounts:Accounts`,
};

/**
 * The history of changes, for Admins (`/admin/history`, ADR-0052): who changed what and when,
 * newest first, a page at a time - everything, an area, one person's, a few days', or one
 * resource's (`?resourceId=`, from the resource's own page). Each entry says what it was about,
 * and what changed before and after, in words.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-audit-history',
  styleUrl: './history.scss',
  templateUrl: './history.html',
})
export class AuditHistory {
  /** Query parameter: one resource's history. */
  readonly resourceId = input<string>();

  private readonly audit = inject(AuditService);
  private readonly accounts = inject(AccountService);
  private readonly resourceService = inject(ResourceService);
  private readonly router = inject(Router);
  private readonly locale = inject(LOCALE_ID);

  protected readonly areas = AUDIT_AREAS.map((area) => ({
    value: area,
    label: AREA_LABELS[area](),
  }));
  protected readonly filters = inject(NonNullableFormBuilder).group({
    area: ['' as AuditArea | ''],
    userId: [''],
    from: [''],
    to: [''],
  });

  protected readonly people = signal<UserAccount[]>([]);
  private readonly entries = signal<AuditEntry[]>([]);
  private readonly names = signal<Record<string, string>>({});
  private readonly resourceNames = signal<Record<string, string>>({});
  private readonly types = signal<Record<string, string>>({});
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly failed = signal(false);

  /** The resource the history is narrowed to, by name once it's known. */
  protected readonly resourceName = computed(() => {
    const id = this.resourceId();
    return id ? (this.resourceNames()[id] ?? null) : null;
  });

  protected readonly rows = computed(() => {
    const context = {
      people: this.names(),
      resources: this.resourceNames(),
      types: this.types(),
      locale: this.locale,
    };
    return this.entries().map((entry) => ({
      entry,
      entity: entityLabel(entry.entityType),
      subject: subject(entry, context),
      action: actionLabel(entry.action),
      who: actorLabel(entry, context.people),
      when: this.when(entry.at),
      lines: changeLines(entry, context),
    }));
  });

  constructor() {
    this.accounts
      .getAccounts()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (people) =>
          this.people.set([...people].sort((a, b) => a.displayName.localeCompare(b.displayName))),
        error: () => this.people.set([]),
      });
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (types) => this.types.set(Object.fromEntries(types.map((t) => [t.id, t.name]))),
        error: () => undefined,
      });

    combineLatest([
      this.filters.valueChanges.pipe(startWith(this.filters.getRawValue())),
      toObservable(this.resourceId),
    ])
      .pipe(
        map(() => this.filter()),
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
        }),
        // switchMap: the answer to a filter changed again meanwhile doesn't count.
        switchMap((filter) =>
          this.audit.getPage(filter).pipe(
            catchError(() => {
              this.failed.set(true);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((page) => {
        this.loading.set(false);
        this.entries.set(page?.items ?? []);
        this.names.set(page?.people ?? {});
        this.resourceNames.set(page?.resources ?? {});
        this.hasMore.set(page?.hasMore ?? false);
      });
  }

  protected showOlder(): void {
    const last = this.entries().at(-1);
    if (!last || this.loadingMore()) return;

    this.loadingMore.set(true);
    this.audit
      .getPage(this.filter(), last.id)
      .pipe(finalize(() => this.loadingMore.set(false)))
      .subscribe({
        next: (page) => {
          this.entries.update((entries) => [...entries, ...page.items]);
          this.names.update((names) => ({ ...names, ...page.people }));
          this.resourceNames.update((names) => ({ ...names, ...page.resources }));
          this.hasMore.set(page.hasMore);
        },
        // The error interceptor has said why; the button is there to try again.
        error: () => undefined,
      });
  }

  /** Back to everything, the resource included. */
  protected clear(): void {
    this.filters.reset();
    if (this.resourceId()) {
      void this.router.navigate([], {
        queryParams: { resourceId: null },
        queryParamsHandling: 'merge',
      });
    }
  }

  private filter(): AuditFilter {
    const { area, userId, from, to } = this.filters.getRawValue();
    return {
      area: area || null,
      userId: userId || null,
      resourceId: this.resourceId() ?? null,
      // Whole days, in the screen's own zone: from the first day's start to the last day's end.
      from: from ? new Date(`${from}T00:00`).toISOString() : null,
      to: to ? dayAfter(to).toISOString() : null,
    };
  }

  /** "14:30" today, "Mon 19 Oct, 14:30" on another day. */
  private when(value: string): string {
    const date = new Date(value);
    const today = new Date().toDateString() === date.toDateString();
    return formatDate(date, today ? 'HH:mm' : 'EEE d MMM y, HH:mm', this.locale);
  }
}

/** The local midnight after a picked day: a day on the calendar, 23 or 25 hours when clocks change. */
function dayAfter(day: string): Date {
  const next = new Date(`${day}T00:00`);
  next.setDate(next.getDate() + 1);
  return next;
}
