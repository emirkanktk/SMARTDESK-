import { Injectable, Injector, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { EMPTY, Subject, catchError, map, merge, of, switchMap } from 'rxjs';
import { Roles } from '../constants/roles';
import { issueCategoryLabel } from '../i18n/labels';
import { IssueReport } from '../models/issue.model';
import { AuthService } from './auth.service';
import { IssueService } from './issue.service';
import { NotificationService } from './notification.service';
import { ReservationHubService } from './reservation-hub.service';

/** A new problem's toast stays up as long as a reminder's: it's easy to miss otherwise. */
export const ISSUE_TOAST_MS = 15_000;

/**
 * For those who look after resources - Admins, and Managers for their team's own (ADR-0037):
 * how many reported problems nobody has taken up yet, for the menu, and a toast the moment a
 * new one comes in, plus a desktop notification when the browser allows one.
 */
@Injectable({ providedIn: 'root' })
export class IssueAlertsService {
  private readonly auth = inject(AuthService);
  private readonly hub = inject(ReservationHubService);
  private readonly issues = inject(IssueService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  private readonly openSignal = signal(0);
  /** Reported and not taken up yet; 0 for anyone who doesn't look after resources. */
  readonly open = this.openSignal.asReadonly();

  /** Each value asks the API for the count again, or sets it to 0. */
  private readonly counts = new Subject<'load' | 'clear'>();
  private started = false;

  /** Called once by the app; later calls do nothing. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    // switchMap: only the newest answer counts, e.g. after signing out during a load.
    this.counts
      .pipe(
        switchMap((action) =>
          action === 'clear'
            ? of(0)
            : this.issues.getAll({ status: 'Open' }).pipe(
                map((open) => open.length),
                catchError(() => EMPTY),
              ),
        ),
      )
      .subscribe((open) => this.openSignal.set(open));

    effect(
      () => {
        const looksAfter =
          this.auth.isAuthenticated() &&
          !this.auth.passwordChangeRequired() &&
          (this.auth.hasRole(Roles.Admin) || this.auth.hasRole(Roles.Manager));
        untracked(() => this.counts.next(looksAfter ? 'load' : 'clear'));
      },
      { injector: this.injector },
    );

    this.hub.issueReported$.subscribe((issue) => this.announce(issue));
    // A report taken up, resolved or new changes the count; so may anything missed while the
    // connection was down.
    merge(this.hub.issueReported$, this.hub.issueUpdated$, this.hub.reconnected$).subscribe(() =>
      this.counts.next('load'),
    );
  }

  private announce(issue: IssueReport): void {
    const what = issueCategoryLabel(issue.category);
    const text = issue.isBlocking
      ? $localize`:@@issueAlerts.newBlocking:New problem at ${issue.resourceName}:resource:: ${what}:category:. It can't be used until it's fixed.`
      : $localize`:@@issueAlerts.new:New problem at ${issue.resourceName}:resource:: ${what}:category:.`;
    this.notifications.info(text, ISSUE_TOAST_MS);

    if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      // The tag makes several open tabs show one notification, not one each.
      const desktop = new Notification(
        $localize`:@@issueAlerts.desktopTitle:Problem reported in SmartDesk`,
        { body: `${text} ${issue.description}`, tag: `issue-${issue.id}` },
      );
      desktop.onclick = () => {
        window.focus();
        void this.router.navigateByUrl('/issues');
        desktop.close();
      };
    }
  }
}
