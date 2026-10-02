import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { Reservation } from '../models/reservation.model';
import {
  AvailabilityQuery,
  CheckInCode,
  CreateResourceRequest,
  Resource,
  ResourceFilter,
  ResourceStatus,
  ResourceType,
  UpdateResourceRequest,
} from '../models/resource.model';

@Injectable({ providedIn: 'root' })
export class ResourceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/resources`;

  getResourceTypes(): Observable<ResourceType[]> {
    return this.http.get<ResourceType[]>(`${environment.apiUrl}/resource-types`);
  }

  getResources(filter: ResourceFilter = {}): Observable<Resource[]> {
    let params = new HttpParams();
    if (filter.resourceTypeId) {
      params = params.set('resourceTypeId', filter.resourceTypeId);
    }
    if (filter.status) {
      params = params.set('status', filter.status);
    }
    return this.http.get<Resource[]>(this.baseUrl, { params });
  }

  /**
   * Active resources with no confirmed reservation overlapping [start, end). A snapshot:
   * a slot shown as free can still be taken before it's booked.
   */
  getAvailable(query: AvailabilityQuery): Observable<Resource[]> {
    let params = new HttpParams()
      .set('startDateTime', query.startDateTime)
      .set('endDateTime', query.endDateTime);
    if (query.resourceTypeId) {
      params = params.set('resourceTypeId', query.resourceTypeId);
    }
    if (query.minCapacity) {
      params = params.set('minCapacity', query.minCapacity);
    }
    return this.http.get<Resource[]>(`${this.baseUrl}/available`, { params });
  }

  getResource(id: string): Observable<Resource> {
    return this.http.get<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  create(request: CreateResourceRequest): Observable<Resource> {
    return this.http.post<Resource>(this.baseUrl, request);
  }

  update(id: string, request: UpdateResourceRequest): Observable<Resource> {
    return this.http.put<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}`, request);
  }

  /** Which teams may use it; an empty list makes it shared by everyone. Admin only. */
  setTeams(id: string, teamIds: string[]): Observable<Resource> {
    return this.http.patch<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}/teams`, {
      teamIds,
    });
  }

  /** Sets or replaces its photo. Whoever may edit the resource may change its photo. */
  setPhoto(id: string, photo: Blob): Observable<Resource> {
    const form = new FormData();
    form.append('photo', photo, `photo.${photo.type.split('/')[1] ?? 'jpg'}`);
    return this.http.put<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}/photo`, form);
  }

  /** Removes its photo, so its type's picture shows again. */
  removePhoto(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}/photo`);
  }

  /**
   * Changes only the status, so it can't overwrite someone else's edit. Admins, or a manager for
   * a resource that belongs to their team alone (ADR-0022).
   */
  changeStatus(id: string, status: ResourceStatus): Observable<Resource> {
    return this.http.patch<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}/status`, {
      status,
    });
  }

  /** Its QR code to print (ADR-0030). For whoever may edit the resource. */
  getCheckInCode(id: string): Observable<CheckInCode> {
    return this.http.get<CheckInCode>(`${this.baseUrl}/${encodeURIComponent(id)}/check-in-code`);
  }

  /** A new QR code; the one already printed stops working at once. */
  replaceCheckInCode(id: string): Observable<CheckInCode> {
    return this.http.post<CheckInCode>(
      `${this.baseUrl}/${encodeURIComponent(id)}/check-in-code`,
      null,
    );
  }

  /**
   * Checks the caller in to their booking of the resource that's open for it now, with the
   * secret from its QR code. The check-in page shows why when it can't, so no toast.
   */
  checkIn(id: string, code: string): Observable<Reservation> {
    return this.http.post<Reservation>(
      `${this.baseUrl}/${encodeURIComponent(id)}/check-in`,
      { code },
      { context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true) },
    );
  }
}
