import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { LOBBY_TOKEN_HEADER, Lobby, LobbyScreen, NewLobbyScreen } from '../models/lobby.model';

/**
 * Lobby screens (ADR-0064): what one shows, asked by the screen with its own token and no user,
 * and setting them up and taking them down, for Admins.
 */
@Injectable({ providedIn: 'root' })
export class LobbyService {
  private readonly http = inject(HttpClient);
  private readonly screensUrl = `${environment.apiUrl}/lobby-screens`;

  /** 404 when the token isn't a screen's any more; the screen says so itself. */
  get(token: string, timeZone: string): Observable<Lobby> {
    return this.http.get<Lobby>(`${environment.apiUrl}/lobby`, {
      headers: new HttpHeaders({ [LOBBY_TOKEN_HEADER]: token }),
      params: new HttpParams().set('timeZone', timeZone),
      context: new HttpContext().set(ANONYMOUS_REQUEST, true).set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  list(): Observable<LobbyScreen[]> {
    return this.http.get<LobbyScreen[]>(this.screensUrl);
  }

  /** The page shows what went wrong with the form itself. */
  create(name: string, location: string | null): Observable<NewLobbyScreen> {
    return this.http.post<NewLobbyScreen>(
      this.screensUrl,
      { name, location },
      { context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true) },
    );
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.screensUrl}/${encodeURIComponent(id)}`);
  }
}
