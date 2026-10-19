import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditFilter, AuditPage } from '../models/audit.model';

/** The history of changes (ADR-0052), for Admins: newest first, a page at a time. */
@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);

  /** `before`: the id of the last entry already shown, for the next page. */
  getPage(filter: AuditFilter = {}, before?: number): Observable<AuditPage> {
    let params = new HttpParams();
    for (const key of ['area', 'resourceId', 'userId', 'from', 'to'] as const) {
      const value = filter[key];
      if (value) params = params.set(key, value);
    }
    if (before !== undefined) params = params.set('before', before);
    return this.http.get<AuditPage>(`${environment.apiUrl}/audit`, { params });
  }
}
