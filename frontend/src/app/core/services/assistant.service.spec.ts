import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { AssistantService } from './assistant.service';

describe('AssistantService', () => {
  let service: AssistantService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/assistant`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AssistantService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks whether the server has an assistant, without a toast if it fails', () => {
    let enabled: boolean | undefined;
    service.getStatus().subscribe((status) => (enabled = status.enabled));

    const req = http.expectOne(base);
    expect(req.request.method).toBe('GET');
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush({ enabled: true });
    expect(enabled).toBe(true);
  });

  it('sends the whole conversation and the time zone, and shows its own errors', () => {
    const messages = [
      { role: 'user' as const, text: 'A room for 4 tomorrow at 10' },
      { role: 'assistant' as const, text: 'Room A is free.' },
      { role: 'user' as const, text: 'Book it' },
    ];

    service.ask(messages, 'Europe/Istanbul').subscribe();

    const req = http.expectOne(`${base}/messages`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ messages, timeZone: 'Europe/Istanbul' });
    expect(req.request.context.get(SKIP_ERROR_NOTIFICATION)).toBe(true);
    req.flush({ reply: 'The card is ready.', proposals: [] });
  });

  it("uses the browser's time zone unless told otherwise", () => {
    service.ask([{ role: 'user', text: 'Hi' }]).subscribe();

    const req = http.expectOne(`${base}/messages`);
    expect(req.request.body.timeZone).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
    req.flush({ reply: 'Hello!', proposals: [] });
  });
});
