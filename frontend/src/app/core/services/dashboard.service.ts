import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Dashboard } from '../models/dashboard.model';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  /**
   * Today's overview. `timeZone` (an IANA id) decides what "today" means - the server runs
   * on UTC, so without it 01:30 in Istanbul would still count as yesterday.
   */
  getDashboard(timeZone: string): Observable<Dashboard> {
    const params = new HttpParams().set('timeZone', timeZone);
    return this.http.get<Dashboard>(`${environment.apiUrl}/dashboard`, { params });
  }
}

/** The browser's IANA time zone, e.g. "Europe/Istanbul". */
export function browserTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
