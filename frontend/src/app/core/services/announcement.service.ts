import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { Announcement, NewAnnouncement } from '../models/announcement.model';

/**
 * Announcements (ADR-0065): the ones showing now, for everyone's dashboard, and posting and taking
 * them down, for Admins.
 */
@Injectable({ providedIn: 'root' })
export class AnnouncementService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/announcements`;

  /** Newest first. Asked quietly: the dashboard works without them. */
  current(): Observable<Announcement[]> {
    return this.http.get<Announcement[]>(this.url, {
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  /** Every one, ended ones too, newest first: for Admins. */
  all(): Observable<Announcement[]> {
    return this.http.get<Announcement[]>(`${this.url}/all`);
  }

  /** The page shows what went wrong with the form itself. */
  create(announcement: NewAnnouncement): Observable<Announcement> {
    return this.http.post<Announcement>(this.url, announcement, {
      context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
    });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${encodeURIComponent(id)}`);
  }
}
