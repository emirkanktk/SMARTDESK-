import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AssistantMessage, AssistantReply, AssistantStatus } from '../models/assistant.model';

/**
 * The booking assistant (ADR-0029). The server keeps no conversation: each question sends
 * everything said so far. Its errors are shown in the conversation, not as toasts.
 */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/assistant`;
  private readonly quiet = () => new HttpContext().set(SKIP_ERROR_NOTIFICATION, true);

  /** Whether this server has an assistant; the app shows it only then. */
  getStatus(): Observable<AssistantStatus> {
    return this.http.get<AssistantStatus>(this.baseUrl, { context: this.quiet() });
  }

  /**
   * The conversation so far, ending with the user's new message. Times are worked out in
   * `timeZone` ("tomorrow at 10"), the browser's own unless given.
   */
  ask(
    messages: readonly AssistantMessage[],
    timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone,
  ): Observable<AssistantReply> {
    return this.http.post<AssistantReply>(
      `${this.baseUrl}/messages`,
      { messages, timeZone },
      { context: this.quiet() },
    );
  }
}
