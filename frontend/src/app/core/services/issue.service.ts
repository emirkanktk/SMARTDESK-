import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { IssueFilter, IssueReport, IssueStatus, NewIssueReport } from '../models/issue.model';

/** Problems reported at resources, and following them up (ADR-0037). */
@Injectable({ providedIn: 'root' })
export class IssueService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/issues`;

  /** The problems still open on a resource, for anyone who may use it. */
  getOpen(resourceId: string): Observable<IssueReport[]> {
    return this.http.get<IssueReport[]>(this.resourceUrl(resourceId));
  }

  /** Reports a problem. The form says why when it can't, so no toast. */
  report(resourceId: string, report: NewIssueReport): Observable<IssueReport> {
    const form = new FormData();
    form.append('category', report.category);
    form.append('description', report.description);
    form.append('isBlocking', String(report.isBlocking));
    if (report.photo) {
      form.append('photo', report.photo, `photo.${report.photo.type.split('/')[1] ?? 'jpg'}`);
    }
    return this.http.post<IssueReport>(this.resourceUrl(resourceId), form, {
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  /** The reports on the resources the caller manages: all of them for an Admin. */
  getAll(filter: IssueFilter = {}): Observable<IssueReport[]> {
    let params = new HttpParams();
    if (filter.status) {
      params = params.set('status', filter.status);
    }
    if (filter.resourceId) {
      params = params.set('resourceId', filter.resourceId);
    }
    return this.http.get<IssueReport[]>(this.baseUrl, { params });
  }

  /** Open and InProgress back and forth, then Resolved, which tells whoever reported it. */
  updateStatus(id: string, status: IssueStatus): Observable<IssueReport> {
    return this.http.patch<IssueReport>(`${this.baseUrl}/${encodeURIComponent(id)}`, { status });
  }

  /** A report's photo, fetched with the token like a resource's (ADR-0027). */
  getPhoto(id: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${encodeURIComponent(id)}/photo`, {
      responseType: 'blob',
    });
  }

  private resourceUrl(resourceId: string): string {
    return `${environment.apiUrl}/resources/${encodeURIComponent(resourceId)}/issues`;
  }
}
