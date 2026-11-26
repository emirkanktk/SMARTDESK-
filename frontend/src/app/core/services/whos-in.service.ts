import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { WhosIn } from '../models/whos-in.model';
import { browserTimeZone } from './dashboard.service';

/**
 * Who's in the office on a day (ADR-0063), that day as the browser's clock has it. The page says
 * itself when it can't load, so a failure isn't toasted too.
 */
@Injectable({ providedIn: 'root' })
export class WhosInService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/whos-in`;

  /** `date`: "2026-11-26". */
  get(date: string): Observable<WhosIn> {
    return this.http.get<WhosIn>(this.baseUrl, {
      params: { date, timeZone: browserTimeZone() },
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }
}
