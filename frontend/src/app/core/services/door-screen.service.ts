import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { DOOR_TOKEN_HEADER, DoorScreen } from '../models/door-screen.model';

/** The door screen's one call (ADR-0034): no user, just the screen's token. */
@Injectable({ providedIn: 'root' })
export class DoorScreenService {
  private readonly http = inject(HttpClient);

  /** 404 when the token isn't the resource's current screen's; the screen says so itself. */
  get(resourceId: string, token: string, timeZone: string): Observable<DoorScreen> {
    return this.http.get<DoorScreen>(
      `${environment.apiUrl}/door/${encodeURIComponent(resourceId)}`,
      {
        headers: new HttpHeaders({ [DOOR_TOKEN_HEADER]: token }),
        params: new HttpParams().set('timeZone', timeZone),
        context: new HttpContext().set(ANONYMOUS_REQUEST, true).set(SKIP_ERROR_NOTIFICATION, true),
      },
    );
  }
}
