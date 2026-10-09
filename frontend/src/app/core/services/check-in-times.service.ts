import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, firstValueFrom, map, of, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ANONYMOUS_REQUEST, SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { CheckInTimes, setCheckInTimes } from '../models/reservation.model';

/** How long the start waits for the times before it goes on with the defaults. */
export const CHECK_IN_TIMES_WAIT_MS = 5_000;

/**
 * Reads the check-in times the API uses (ADR-0040) once, as the app starts, so every screen
 * says and counts with the same ones: the grace is a setting, 15 minutes unless a demo makes it
 * one. Signed out too: a door screen has no user.
 */
@Injectable({ providedIn: 'root' })
export class CheckInTimesService {
  private readonly http = inject(HttpClient);

  /** Never fails: without an answer the defaults stay, and nothing else waits on it. */
  load(): Promise<void> {
    const context = new HttpContext()
      .set(ANONYMOUS_REQUEST, true)
      .set(SKIP_ERROR_NOTIFICATION, true);
    return firstValueFrom(
      this.http.get<CheckInTimes>(`${environment.apiUrl}/check-in/times`, { context }).pipe(
        timeout(CHECK_IN_TIMES_WAIT_MS),
        map((times) => setCheckInTimes(times)),
        catchError(() => of(undefined)),
      ),
      { defaultValue: undefined },
    );
  }
}
