import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { DoorBooking } from '../models/door-screen.model';
import { CHECK_IN_CODE_HEADER, DoorVisit } from '../models/door-visit.model';
import { IssueReport, NewIssueReport } from '../models/issue.model';
import { issueReportForm } from './issue.service';

/**
 * The calls of the page a QR code opens without an account (ADR-0050): no user, just the code it
 * was opened with. The page says itself why one didn't go through, so no toasts.
 */
@Injectable({ providedIn: 'root' })
export class DoorVisitService {
  private readonly http = inject(HttpClient);

  /** 404 when the code isn't the resource's, or no longer is. */
  get(resourceId: string, code: string, timeZone: string): Observable<DoorVisit> {
    return this.http.get<DoorVisit>(`${this.url(resourceId)}/visit`, {
      headers: new HttpHeaders({ [CHECK_IN_CODE_HEADER]: code }),
      params: new HttpParams().set('timeZone', timeZone),
      context: withoutAccount(),
    });
  }

  /** Checks in the booking waiting for it now, whoever's it is: the answer is its times. */
  checkIn(resourceId: string, code: string): Observable<DoorBooking> {
    return this.http.post<DoorBooking>(
      `${this.url(resourceId)}/check-in`,
      { code },
      { context: withoutAccount() },
    );
  }

  report(resourceId: string, code: string, report: NewIssueReport): Observable<IssueReport> {
    const form = issueReportForm(report);
    form.append('code', code);
    return this.http.post<IssueReport>(`${this.url(resourceId)}/issues`, form, {
      context: withoutAccount(),
    });
  }

  private url(resourceId: string): string {
    return `${environment.apiUrl}/door/${encodeURIComponent(resourceId)}`;
  }
}

function withoutAccount(): HttpContext {
  return new HttpContext().set(ANONYMOUS_REQUEST, true).set(SKIP_ERROR_NOTIFICATION, true);
}
