import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { Floor } from '../models/floor-plan.model';

/**
 * Floors and their plans (ADR-0041). A floor is a location the resources share, so it goes in
 * the query string. A plan's picture is only for those who may use a resource on that floor,
 * so it's fetched with the token, like a resource's photo.
 */
@Injectable({ providedIn: 'root' })
export class FloorPlanService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/floor-plans`;

  /** The floors of the resources the caller may use, with each plan's size. */
  getFloors(): Observable<Floor[]> {
    return this.http.get<Floor[]>(this.baseUrl);
  }

  /**
   * A floor's plan at this version (its `updatedAt`, which keeps the browser from offering an
   * older one). One that fails to load isn't worth a message: the floor shows its grid instead.
   */
  getPlan(location: string, version: string): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/image`, {
      params: { location, v: version },
      responseType: 'blob',
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  /** Sets or replaces a floor's plan; Admins only. The page says why when it can't, so no toast. */
  setPlan(location: string, plan: Blob): Observable<Floor> {
    const form = new FormData();
    form.append('plan', plan, `plan.${plan.type.split('/')[1] ?? 'png'}`);
    return this.http.put<Floor>(`${this.baseUrl}/image`, form, {
      params: { location },
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  /** Removes a floor's plan; its resources keep their places, for the next one. */
  removePlan(location: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/image`, { params: { location } });
  }
}
