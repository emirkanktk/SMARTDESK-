import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { UsageReport, UsageReportQuery } from '../models/report.model';

@Injectable({ providedIn: 'root' })
export class ReportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/reports`;

  /** Admin only: how resources were used over whole local days, `from` to `to` included. */
  getUsage(query: UsageReportQuery): Observable<UsageReport> {
    let params = new HttpParams()
      .set('from', query.from)
      .set('to', query.to)
      .set('timeZone', query.timeZone);
    if (query.resourceTypeId) {
      params = params.set('resourceTypeId', query.resourceTypeId);
    }
    return this.http.get<UsageReport>(`${this.baseUrl}/usage`, { params });
  }
}
