import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  ElementRef,
  LOCALE_ID,
  OnInit,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Observable, Subscription, finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AssistantMessage, AssistantProposal } from '../../core/models/assistant.model';
import { AssistantService } from '../../core/services/assistant.service';
import { AuthService } from '../../core/services/auth.service';
import { ReservationService } from '../../core/services/reservation.service';
import { Icon } from '../../shared/components/icon/icon';
import { AssistantCard, CardState, describeWhen } from './assistant-card';
import { ReplyBlock, formatReply } from './reply-format';

/** The longest message the API takes (AskAssistantCommandValidator). */
const MAX_LENGTH = 2000;

const UNAVAILABLE = $localize`:@@assistant.unavailable:The assistant can't answer right now. Try again in a moment.`;

interface Card {
  proposal: AssistantProposal;
  state: CardState;
  error: string | null;
}

/**
 * One line of the conversation. A "note" is what the user did with a card ("Booked: Room A,
 * Mon 5 Oct, 10:00-11:00"): it goes to the assistant as the user's, so it knows.
 */
interface Entry {
  id: number;
  kind: 'user' | 'assistant' | 'note';
  text: string;
  blocks: ReplyBlock[];
  cards: Card[];
}

interface Failure {
  message: string;
  /** Worth sending again: the server was busy or out of reach, not refusing the question. */
  retry: boolean;
}

/**
 * The booking assistant (ADR-0029): a button in the corner of every signed-in screen that
 * opens a conversation, shown only when the server has an assistant. It answers from what
 * the user may see and offers bookings and cancellations as cards; a card's button does it
 * through the same endpoints as the screens. The conversation lasts until sign-out or a new
 * one is started, and the server keeps none of it.
 */
@Component({
  selector: 'app-assistant',
  imports: [AssistantCard, Icon, NgTemplateOutlet],
  templateUrl: './assistant.html',
  styleUrl: './assistant.scss',
  host: { '(keydown.escape)': 'close()' },
})
export class Assistant implements OnInit {
  private readonly assistantService = inject(AssistantService);
  private readonly reservationService = inject(ReservationService);
  private readonly authService = inject(AuthService);
  private readonly locale = inject(LOCALE_ID);

  private readonly launcher = viewChild<ElementRef<HTMLButtonElement>>('launcher');
  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');

  /** Whether the assistant is shown, so the page can keep its corner clear. */
  readonly available = output<boolean>();

  protected readonly enabled = signal(false);
  protected readonly open = signal(false);
  protected readonly entries = signal<Entry[]>([]);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly failure = signal<Failure | null>(null);
  protected readonly maxLength = MAX_LENGTH;

  protected readonly greeting = computed(() => {
    const name = this.authService.currentUser()?.firstName?.trim();
    return name
      ? $localize`:@@assistant.greetingNamed:Hi ${name}:name:! I can find a free room, desk or car and get the booking ready. You confirm it.`
      : $localize`:@@assistant.greeting:Hi! I can find a free room, desk or car and get the booking ready. You confirm it.`;
  });

  protected readonly suggestions = [
    $localize`:@@assistant.suggestion.room:Find a meeting room for 4 people tomorrow at 10:00`,
    $localize`:@@assistant.suggestion.desk:Is a desk free this afternoon?`,
    $localize`:@@assistant.suggestion.mine:What have I booked this week?`,
  ];

  private nextId = 1;
  private pending: Subscription | null = null;

  ngOnInit(): void {
    this.assistantService.getStatus().subscribe({
      next: ({ enabled }) => {
        this.enabled.set(enabled);
        this.available.emit(enabled);
      },
      // No assistant on this server (an API from before it), or no answer: no button.
      error: () => undefined,
    });
  }

  protected toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.open.set(true);
      // The panel's parts exist once it has rendered.
      setTimeout(() => {
        this.input()?.nativeElement.focus();
        this.scrollToEnd();
      });
    }
  }

  /** Closes the panel and gives focus back to the button; the conversation stays. */
  protected close(): void {
    if (this.open()) {
      this.open.set(false);
      this.launcher()?.nativeElement.focus();
    }
  }

  protected newConversation(): void {
    this.pending?.unsubscribe();
    this.entries.set([]);
    this.failure.set(null);
    this.draft.set('');
    this.input()?.nativeElement.focus();
  }

  /** Enter sends; Shift+Enter starts a new line. Not while an input method is composing. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.send();
    }
  }

  protected send(text = this.draft()): void {
    const message = text.trim();
    if (!message || this.busy()) {
      return;
    }
    this.draft.set('');
    // The field too: typed and sent before the page next updates, the binding would still
    // hold '' from before and leave the text standing.
    const field = this.input()?.nativeElement;
    if (field) {
      field.value = '';
    }
    this.add('user', message);
    this.ask();
  }

  protected retry(): void {
    this.ask();
  }

  /** The card's button: books or cancels as the screens do, with the same checks. */
  protected confirm(entry: Entry, index: number): void {
    const { proposal } = entry.cards[index];
    const resource = proposal.resourceName;
    const when = describeWhen(proposal, this.locale);
    const booking = proposal.kind === 'book';
    if (
      !booking &&
      !confirm(
        $localize`:@@reservationForm.confirmCancel:Cancel your ${resource}:resource: reservation for ${when}:when:?`,
      )
    ) {
      return;
    }

    this.updateCard(entry.id, index, { state: 'working', error: null });
    const done: Observable<unknown> = booking
      ? this.reservationService.create({
          resourceId: proposal.resourceId!,
          startDateTime: proposal.startDateTime,
          endDateTime: proposal.endDateTime,
          note: proposal.note,
          reminderMinutes: null,
        })
      : this.reservationService.cancel(proposal.reservationId!);
    done.subscribe({
      next: () => {
        this.updateCard(entry.id, index, { state: 'done', error: null });
        this.add(
          'note',
          booking
            ? $localize`:@@assistant.note.booked:Booked: ${resource}:resource:, ${when}:when:`
            : $localize`:@@assistant.note.cancelled:Cancelled: ${resource}:resource:, ${when}:when:`,
        );
      },
      error: (error: unknown) => {
        // 400 or 409: the server's reason, e.g. someone was faster. Other failures already
        // raised a toast; the card says it didn't work.
        const reason =
          error instanceof ApiError && (error.status === 400 || error.status === 409)
            ? firstMessage(error)
            : booking
              ? $localize`:@@reservationForm.saveFailed:The reservation could not be saved.`
              : $localize`:@@reservationForm.cancelFailed:The reservation could not be cancelled.`;
        this.updateCard(entry.id, index, { state: 'failed', error: reason });
      },
    });
  }

  private ask(): void {
    const conversation: AssistantMessage[] = this.entries().map((entry) => ({
      role: entry.kind === 'assistant' ? 'assistant' : 'user',
      text: entry.text,
    }));
    this.busy.set(true);
    this.failure.set(null);
    this.scrollToEnd();
    this.pending = this.assistantService
      .ask(conversation)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (reply) => this.add('assistant', reply.reply, reply.proposals),
        error: (error: unknown) => {
          this.failure.set(failureFor(error));
          this.scrollToEnd();
        },
      });
  }

  private add(kind: Entry['kind'], text: string, proposals: AssistantProposal[] = []): void {
    const entry: Entry = {
      id: this.nextId++,
      kind,
      text,
      blocks: kind === 'assistant' ? formatReply(text) : [],
      cards: proposals.map((proposal) => ({ proposal, state: 'ready', error: null })),
    };
    this.entries.update((entries) => [...entries, entry]);
    this.scrollToEnd();
  }

  private updateCard(entryId: number, index: number, change: Omit<Card, 'proposal'>): void {
    this.entries.update((entries) =>
      entries.map((entry) =>
        entry.id === entryId
          ? {
              ...entry,
              cards: entry.cards.map((card, i) => (i === index ? { ...card, ...change } : card)),
            }
          : entry,
      ),
    );
  }

  private scrollToEnd(): void {
    setTimeout(() => {
      const log = this.log()?.nativeElement;
      if (log) {
        log.scrollTop = log.scrollHeight;
      }
    });
  }
}

function failureFor(error: unknown): Failure {
  if (!(error instanceof ApiError)) {
    return { message: UNAVAILABLE, retry: true };
  }
  // 0: no answer. 429: asked too often. 5xx: the model is busy or down. Worth another go.
  const retry = error.status === 0 || error.status === 429 || error.status >= 500;
  const message = error.status === 0 ? error.title : firstMessage(error);
  return { message: error.status >= 500 && !error.detail ? UNAVAILABLE : message, retry };
}

/** A validation message, else the detail, else the title. */
function firstMessage(error: ApiError): string {
  return Object.values(error.fieldErrors)[0]?.[0] ?? error.detail ?? error.title;
}
