import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateResourceBlockRequest,
  ResourceBlock,
  ResourceBlockQuery,
} from '../models/resource-block.model';

@Injectable({ providedIn: 'root' })
export class ResourceBlockService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/resource-blocks`;

  /** Blocks overlapping [from, to), earliest first, optionally for one resource. Any user. */
  getBlocks(query: ResourceBlockQuery): Observable<ResourceBlock[]> {
    let params = new HttpParams().set('from', query.from).set('to', query.to);
    if (query.resourceId) {
      params = params.set('resourceId', query.resourceId);
    }
    return this.http.get<ResourceBlock[]>(this.baseUrl, { params });
  }

  /**
   * Admin only. 409 when another block or a Confirmed reservation overlaps the window - its
   * detail is written to be shown as-is.
   */
  create(request: CreateResourceBlockRequest): Observable<ResourceBlock> {
    return this.http.post<ResourceBlock>(this.baseUrl, request);
  }

  /** Admin only. The resource can be booked in that window again. */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
