import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AvailabilityQuery,
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

  /** Changes only the status, so it can't overwrite another admin's edit. Admin only. */
  changeStatus(id: string, status: ResourceStatus): Observable<Resource> {
    return this.http.patch<Resource>(`${this.baseUrl}/${encodeURIComponent(id)}/status`, {
      status,
    });
  }
}
