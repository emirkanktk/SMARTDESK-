import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import {
  BookingRules,
  OfficeHours,
  OfficeHoursRequest,
  ResourceTypeRules,
  ResourceTypeRulesRequest,
} from '../models/booking-rules.model';

/**
 * The rules bookings keep to (ADR-0046): anyone reads them, so the booking form can show them;
 * an Admin sets them.
 */
@Injectable({ providedIn: 'root' })
export class BookingRulesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/booking-rules`;

  /**
   * `quiet`: a failure raises no toast, for a screen that does without the rules - the API
   * keeps to them anyway.
   */
  get(quiet = false): Observable<BookingRules> {
    const context = new HttpContext().set(SKIP_ERROR_NOTIFICATION, quiet);
    return this.http.get<BookingRules>(this.baseUrl, { context });
  }

  setOfficeHours(request: OfficeHoursRequest): Observable<OfficeHours> {
    return this.http.put<OfficeHours>(`${this.baseUrl}/office-hours`, request);
  }

  setTypeRules(
    resourceTypeId: string,
    request: ResourceTypeRulesRequest,
  ): Observable<ResourceTypeRules> {
    return this.http.put<ResourceTypeRules>(
      `${this.baseUrl}/types/${encodeURIComponent(resourceTypeId)}`,
      request,
    );
  }
}
