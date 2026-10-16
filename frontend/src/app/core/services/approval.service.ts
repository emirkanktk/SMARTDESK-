import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { Reservation } from '../models/reservation.model';

/** Bookings that wait for an Admin's approval (ADR-0049). Admin only. */
@Injectable({ providedIn: 'root' })
export class ApprovalService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/approvals`;

  /**
   * What waits for approval, the soonest start first. `quiet`: a screen that says so itself
   * when it can't load.
   */
  getWaiting(quiet = false): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(this.baseUrl, {
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, quiet),
    });
  }

  /** Booked from now on; whoever asked is told. */
  approve(id: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.baseUrl}/${id}/approve`, null);
  }

  /** Its time free again; whoever asked is told why. */
  decline(id: string, reason: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.baseUrl}/${id}/decline`, { reason });
  }
}
