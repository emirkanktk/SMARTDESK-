import { formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AssistantProposal } from '../../core/models/assistant.model';
import { Icon } from '../../shared/components/icon/icon';

/** Where a card is: waiting for the user, being done, done, or refused by the server. */
export type CardState = 'ready' | 'working' | 'done' | 'failed';

/** "Mon 5 Oct, 10:00-11:30", or with both dates when it spans days. */
export function describeWhen(proposal: AssistantProposal, locale: string): string {
  const start = new Date(proposal.startDateTime);
  const end = new Date(proposal.endDateTime);
  const sameDay = start.toDateString() === end.toDateString();
  const from = formatDate(start, 'EEE d MMM, HH:mm', locale);
  const to = formatDate(end, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', locale);
  return sameDay ? `${from}-${to}` : $localize`:@@common.rangeTo:${from}:start: to ${to}:end:`;
}

/**
 * A booking or a cancellation the assistant offers (ADR-0029). It shows what would happen;
 * only its button does it, and the conversation carries it out through the reservation
 * endpoints, with every rule they apply.
 */
@Component({
  selector: 'app-assistant-card',
  imports: [Icon, RouterLink],
  template: `
    <p class="kind">
      <app-icon [name]="booking() ? 'calendar-plus' : 'calendar'" />
      {{ booking() ? bookingLabel : cancellationLabel }}
    </p>
    <p class="name">{{ proposal().resourceName }}</p>
    <p class="when"><app-icon name="clock" />{{ when() }}</p>
    @if (proposal().note) {
      <p class="note">{{ proposal().note }}</p>
    }
    @if (state() === 'done') {
      <p class="done">
        <app-icon name="check" />
        <span>{{ booking() ? bookedLabel : cancelledLabel }}</span>
        <a routerLink="/reservations/mine" i18n="@@nav.myReservations">My reservations</a>
      </p>
    } @else {
      <button
        type="button"
        class="btn btn-sm"
        [class.btn-primary]="booking()"
        [class.btn-secondary]="!booking()"
        [disabled]="state() === 'working'"
        (click)="confirmed.emit()"
      >
        {{ buttonLabel() }}
      </button>
      @if (state() === 'failed' && error()) {
        <p class="field-error">{{ error() }}</p>
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-1);
      margin-top: var(--space-2);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-left: 3px solid var(--color-primary);
      border-radius: var(--radius);
      background: var(--color-surface);
      box-shadow: var(--shadow-xs);
    }
    :host(.cancel) {
      border-left-color: var(--color-danger);
    }
    p {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
    }
    .kind {
      color: var(--color-text-muted);
      font-size: 0.75rem;
      font-weight: 700;
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }
    .name {
      font-weight: 700;
    }
    .when,
    .note {
      color: var(--color-text-muted);
      font-size: 0.875rem;
    }
    .note {
      display: block;
      font-style: italic;
    }
    button {
      justify-self: start;
      margin-top: var(--space-1);
    }
    .done {
      flex-wrap: wrap;
      color: var(--color-success);
      font-size: 0.875rem;
      font-weight: 600;
    }
    .done a {
      margin-left: auto;
    }
  `,
  host: { '[class.cancel]': '!booking()' },
})
export class AssistantCard {
  private readonly locale = inject(LOCALE_ID);

  readonly proposal = input.required<AssistantProposal>();
  readonly state = input<CardState>('ready');
  /** Why the server refused it, when it did. */
  readonly error = input<string | null>(null);
  /** The user pressed the button: book it, or cancel it. */
  readonly confirmed = output<void>();

  protected readonly booking = computed(() => this.proposal().kind === 'book');
  protected readonly when = computed(() => describeWhen(this.proposal(), this.locale));
  protected readonly buttonLabel = computed(() => {
    const working = this.state() === 'working';
    if (this.booking()) {
      return working
        ? $localize`:@@assistant.card.booking:Reserving…`
        : $localize`:@@assistant.card.book:Reserve`;
    }
    return working
      ? $localize`:@@assistant.card.cancelling:Cancelling…`
      : $localize`:@@assistant.card.cancel:Cancel reservation`;
  });

  protected readonly bookingLabel = $localize`:@@assistant.card.kindBook:New booking`;
  protected readonly cancellationLabel = $localize`:@@assistant.card.kindCancel:Cancellation`;
  protected readonly bookedLabel = $localize`:@@assistant.card.booked:Booked`;
  protected readonly cancelledLabel = $localize`:@@assistant.card.cancelled:Cancelled`;
}
