import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { CalendarSubscription } from '../models/calendar.model';

/** Bookings in a calendar app (ADR-0048): one as an .ics file, or all through a personal link. */
@Injectable({ providedIn: 'root' })
export class CalendarService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/calendar`;

  /**
   * The caller's link, without the link itself; null when they have none. Quiet: the profile
   * card says so itself when it can't load.
   */
  getSubscription(): Observable<CalendarSubscription | null> {
    return this.http.get<CalendarSubscription | null>(`${this.baseUrl}/subscription`, {
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  /** A new link, in this answer only. The old one stops working. */
  createSubscription(): Observable<CalendarSubscription> {
    return this.http.post<CalendarSubscription>(`${this.baseUrl}/subscription`, null);
  }

  deleteSubscription(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/subscription`);
  }

  /** A booking as an .ics file, for its owner or someone invited to it, in the app's language. */
  getReservationFile(reservationId: string): Observable<Blob> {
    return this.http.get(`${environment.apiUrl}/reservations/${reservationId}/calendar`, {
      responseType: 'blob',
    });
  }
}
