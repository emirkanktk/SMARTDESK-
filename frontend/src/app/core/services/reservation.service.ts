import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateReservationRequest,
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
