import { formatDate } from '@angular/common';
import { Component, DestroyRef, LOCALE_ID, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, forkJoin, interval, merge, of } from 'rxjs';
import { issueCategoryLabel, issueStatusLabel } from '../../core/i18n/labels';
import { ApiError } from '../../core/models/api-error.model';
import { IssueReport, IssueStatus, compareIssues } from '../../core/models/issue.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { IssueService } from '../../core/services/issue.service';
import { NotificationService } from '../../core/services/notification.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { Icon } from '../../shared/components/icon/icon';
import { addDays } from '../../shared/utils/date-time';
import { IssuePhoto } from './issue-photo/issue-photo';
import { TakeOutOfUse } from './take-out-of-use/take-out-of-use';

/** Which reports the list shows: the ones still to fix unless asked otherwise. */
export type StatusFilter = 'unresolved' | IssueStatus | 'all';

/** How far ahead the screen looks for a resource's closures. */
const CLOSURE_DAYS = 31;

interface Item {
  issue: IssueReport;
  /** "Reported by Ayşe Yılmaz · Thu 8 Oct, 10:05", and when it was resolved. */
  meta: string;
  /** The blocked window the resource is in now, e.g. one made from this screen. */
  closure: ResourceBlock | null;
  closedUntil: string | null;
}

/**
 * Problems reported at the resources the user looks after (ADR-0037): every one for an Admin,
 * those on their team's own resources for a Manager. Still to fix first, the ones that make a
 * resource unusable on top. Each can be taken up and resolved, which tells whoever reported
 * it; and its resource can be closed until a chosen time, cancelling the bookings in it
 * (ADR-0038), then opened again. New reports and changes come in live.
 */
@Component({
  imports: [Icon, IssuePhoto, TakeOutOfUse],
  selector: 'app-issues',
  styleUrl: './issues.scss',
  templateUrl: './issues.html',
})
export class Issues implements OnInit {
  private readonly issueService = inject(IssueService);
  private readonly blockService = inject(ResourceBlockService);
  private readonly hub = inject(ReservationHubService);
  private readonly notifications = inject(NotificationService);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly categoryLabel = issueCategoryLabel;
  protected readonly statusLabel = issueStatusLabel;

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  private readonly reports = signal<IssueReport[]>([]);
  private readonly closures = signal<ResourceBlock[]>([]);
  protected readonly status = signal<StatusFilter>('unresolved');
  protected readonly resourceId = signal('');
  /** Reports being moved on, so their buttons wait. */
  protected readonly moving = signal<ReadonlySet<string>>(new Set());
  /** The report whose resource is being closed: its form is open. */
  protected readonly closing = signal<string | null>(null);
  protected readonly reopening = signal<string | null>(null);
  private readonly now = signal(Date.now());

  /** The resources that have reports, for the filter. */
  protected readonly resources = computed(() => {
    const byId = new Map(this.reports().map((issue) => [issue.resourceId, issue.resourceName]));
    return [...byId]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name, this.locale));
  });

  protected readonly counts = computed(() => ({
    open: this.reports().filter((issue) => issue.status === 'Open').length,
    inProgress: this.reports().filter((issue) => issue.status === 'InProgress').length,
  }));

  protected readonly items = computed<Item[]>(() => {
    const status = this.status();
    const resourceId = this.resourceId();
    const now = this.now();
    return this.reports()
      .filter((issue) =>
        status === 'all'
          ? true
          : status === 'unresolved'
            ? issue.status !== 'Resolved'
            : issue.status === status,
      )
      .filter((issue) => !resourceId || issue.resourceId === resourceId)
      .map((issue) => {
        const closure = this.closureOf(issue.resourceId, now);
        return {
          issue,
          meta: this.meta(issue),
          closure,
          closedUntil: closure ? this.when(closure.endDateTime) : null,
        };
      });
  });

  ngOnInit(): void {
    this.load();

    // New reports and changes made elsewhere come in as they happen; anything missed while the
    // connection was down, with the list again.
    merge(this.hub.issueReported$, this.hub.issueUpdated$)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((issue) => this.upsert(issue));
    this.hub.reconnected$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    // Closures end by themselves.
    interval(60_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.now.set(Date.now()));
  }

  protected load(): void {
    const now = new Date();
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({
      reports: this.issueService.getAll(),
      // Only to say which resources are closed: the list works without it.
      closures: this.blockService
        .getBlocks({ from: now.toISOString(), to: addDays(now, CLOSURE_DAYS).toISOString() })
        .pipe(catchError(() => of([]))),
    })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ reports, closures }) => {
          this.reports.set([...reports].sort(compareIssues));
          this.closures.set(closures);
          this.now.set(Date.now());
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected setStatus(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value as StatusFilter);
  }

  protected setResource(event: Event): void {
    this.resourceId.set((event.target as HTMLSelectElement).value);
  }

  /** Takes a report up, puts it back, or resolves it, which tells whoever reported it. */
  protected move(issue: IssueReport, status: IssueStatus): void {
    if (
      status === 'Resolved' &&
      !confirm(
        $localize`:@@issues.resolveConfirm:Mark the problem with ${issue.resourceName}:resource: resolved? Whoever reported it is told, and it can't be opened again.`,
      )
    ) {
      return;
    }

    this.moving.update((ids) => new Set(ids).add(issue.id));
    this.issueService
      .updateStatus(issue.id, status)
      .pipe(
        finalize(() =>
          this.moving.update((ids) => {
            const next = new Set(ids);
            next.delete(issue.id);
            return next;
          }),
        ),
      )
      .subscribe({
        next: (updated) => {
          this.upsert(updated);
          if (status === 'Resolved') {
            this.notifications.success(
              $localize`:@@issues.resolved:The problem with ${issue.resourceName}:resource: is resolved.`,
            );
            // Fixed: it can usually be used again. Asked, not done: another problem there may
            // still keep it closed.
            const closure = this.closureOf(issue.resourceId, Date.now());
            if (
              closure &&
              confirm(
                $localize`:@@issues.reopenAfterResolve:${issue.resourceName}:resource: is still closed until ${this.when(closure.endDateTime)}:time:. Open it for booking again now?`,
              )
            ) {
              this.lift(closure);
            }
          }
        },
        // A 409 says why (resolved meanwhile); other errors have their toast already.
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 409) {
            this.notifications.error(error.detail ?? error.title);
            this.load();
          }
        },
      });
  }

  protected onClosed(issue: IssueReport, block: ResourceBlock): void {
    this.closing.set(null);
    this.closures.update((list) => [...list, block]);
    this.now.set(Date.now());
    this.notifications.success(
      $localize`:@@issues.closed:${issue.resourceName}:resource: is closed until ${this.when(block.endDateTime)}:time:.`,
    );
  }

  /** Lifts the closure early, once the problem is fixed: it can be booked again at once. */
  protected reopen(block: ResourceBlock): void {
    if (
      confirm(
        $localize`:@@issues.reopenConfirm:Open ${block.resourceName}:resource: for booking again now? Bookings cancelled when it was closed stay cancelled.`,
      )
    ) {
      this.lift(block);
    }
  }

  private lift(block: ResourceBlock): void {
    this.reopening.set(block.id);
    this.blockService
      .delete(block.id)
      .pipe(finalize(() => this.reopening.set(null)))
      .subscribe({
        next: () => {
          this.closures.update((list) => list.filter((b) => b.id !== block.id));
          this.notifications.success(
            $localize`:@@issues.reopened:${block.resourceName}:resource: can be booked again.`,
          );
        },
        // The error interceptor has shown a toast already.
        error: () => undefined,
      });
  }

  protected photoLabel(issue: IssueReport): string {
    return $localize`:@@issues.photoLabel:Photo of the problem with ${issue.resourceName}:resource:, opens in a new tab`;
  }

  /** The resource's closure now, or from the next moment: one made here starts as it's made. */
  private closureOf(resourceId: string, now: number): ResourceBlock | null {
    return (
      this.closures().find(
        (b) =>
          b.resourceId === resourceId &&
          new Date(b.startDateTime).getTime() <= now + 60_000 &&
          now < new Date(b.endDateTime).getTime(),
      ) ?? null
    );
  }

  private upsert(issue: IssueReport): void {
    this.reports.update((list) =>
      [...list.filter((i) => i.id !== issue.id), issue].sort(compareIssues),
    );
  }

  private meta(issue: IssueReport): string {
    const who = issue.reportedByName ?? $localize`:@@common.someone:Someone`;
    const reported = $localize`:@@issues.reportedBy:Reported by ${who}:name: · ${this.when(issue.createdAt)}:time:`;
    return issue.resolvedAt
      ? `${reported} · ${$localize`:@@issues.resolvedAt:resolved ${this.when(issue.resolvedAt)}:time:`}`
      : reported;
  }

  /** "14:30" today, "Fri 9 Oct, 14:30" on another day. */
  private when(value: string): string {
    const date = new Date(value);
    const sameDay = new Date().toDateString() === date.toDateString();
    return formatDate(date, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
  }
}
