import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { JoinWaitlistRequest, WaitlistEntry } from '../models/waitlist.model';

/**
 * The caller's waiting list (ADR-0043): times they wait for on resources taken then. When one
 * frees up it's booked for whoever joined first, and their inbox says so.
 */
@Injectable({ providedIn: 'root' })
export class WaitlistService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/waitlist`;

  /** What the caller waits for, soonest first; times gone by are left out. */
  getMine(): Observable<WaitlistEntry[]> {
    return this.http.get<WaitlistEntry[]>(this.baseUrl);
  }

  /** A 409 says why not: the time is free, or they're already waiting for it. */
  join(request: JoinWaitlistRequest): Observable<WaitlistEntry> {
    return this.http.post<WaitlistEntry>(this.baseUrl, request);
  }

  leave(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
