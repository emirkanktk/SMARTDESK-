import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CalendarQuery,
  CreateReservationRequest,
  ExtensionOptions,
  Invitee,
  RespondRequest,
  Reservation,
  UpdateReservationRequest,
} from '../models/reservation.model';

@Injectable({ providedIn: 'root' })
export class ReservationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/reservations`;

  /** The caller's reservations - upcoming, past and cancelled - oldest start first. */
  getMine(): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.baseUrl}/mine`);
  }

  /** Other people's meetings the caller is invited to and that aren't over, soonest first. */
  getInvited(): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.baseUrl}/invited`);
  }

  /** How much longer the booking can run: the extension options that still fit (ADR-0033). */
  getExtensionOptions(id: string): Observable<ExtensionOptions> {
    return this.http.get<ExtensionOptions>(`${this.baseUrl}/${encodeURIComponent(id)}/extension`);
  }

  /**
   * Lets the booking run `minutes` longer, for the owner or an Admin. 409 when the resource
   * isn't free for the extra time, its detail written to be shown as-is.
   */
  extend(id: string, minutes: number): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.baseUrl}/${encodeURIComponent(id)}/extend`, {
      minutes,
    });
  }

  /**
   * The caller's answer to a meeting they're invited to; the organizer gets a message. 400
   * when declining without a reason, 403 for someone not on the list, 409 once it's cancelled
   * or over.
   */
  respond(id: string, request: RespondRequest): Observable<Reservation> {
    return this.http.post<Reservation>(
      `${this.baseUrl}/${encodeURIComponent(id)}/response`,
      request,
    );
  }

  /**
   * Who the caller may invite to a booking, by name: a Manager's own team, every active
   * account for an Admin, nobody for an Employee.
   */
  getInvitees(): Observable<Invitee[]> {
    return this.http.get<Invitee[]>(`${this.baseUrl}/invitees`);
  }

  /**
   * The shared calendar: everyone's Confirmed reservations overlapping [from, to), earliest
   * first, optionally for one resource or type.
   */
  getCalendar(query: CalendarQuery): Observable<Reservation[]> {
    let params = new HttpParams().set('from', query.from).set('to', query.to);
    if (query.resourceId) {
      params = params.set('resourceId', query.resourceId);
    }
    if (query.resourceTypeId) {
      params = params.set('resourceTypeId', query.resourceTypeId);
    }
    return this.http.get<Reservation[]>(this.baseUrl, { params });
  }

  /** One reservation in any status; any signed-in user may read it. */
  getReservation(id: string): Observable<Reservation> {
    return this.http.get<Reservation>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  /**
   * Books for the caller. 409 when the resource isn't Active or the slot was taken - its
   * detail is written to be shown as-is.
   */
  create(request: CreateReservationRequest): Observable<Reservation> {
    return this.http.post<Reservation>(this.baseUrl, request);
  }

  /** Full replace, for the owner or an Admin. What may change depends on the reservation's state. */
  update(id: string, request: UpdateReservationRequest): Observable<Reservation> {
    return this.http.put<Reservation>(`${this.baseUrl}/${encodeURIComponent(id)}`, request);
  }

  /**
   * Cancels for the owner or an Admin and returns the cancelled reservation. Cancelling one
   * that is already cancelled is a no-op; 409 once it has ended.
   */
  cancel(id: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.baseUrl}/${encodeURIComponent(id)}/cancel`, null);
  }
}
