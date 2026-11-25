import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { EndedSessions, SignInSession } from '../models/session.model';

/**
 * The devices the user is signed in on (ADR-0061): listing them, and signing one or all the others
 * out from here. The profile shows its own messages, so failures aren't toasted twice.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/auth/sessions`;

  list(): Observable<SignInSession[]> {
    return this.http.get<SignInSession[]>(this.baseUrl, { context: quietly() });
  }

  end(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, { context: quietly() });
  }

  endOthers(): Observable<EndedSessions> {
    return this.http.post<EndedSessions>(`${this.baseUrl}/end-others`, null, {
      context: quietly(),
    });
  }
}

function quietly(): HttpContext {
  return new HttpContext().set(SKIP_ERROR_NOTIFICATION, true);
}
