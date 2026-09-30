/** `GET /api/assistant`: whether this server has an assistant (ADR-0029). */
export interface AssistantStatus {
  enabled: boolean;
}

export type AssistantRole = 'user' | 'assistant';

/** One message of the conversation, as the API takes it. */
export interface AssistantMessage {
  role: AssistantRole;
  text: string;
}

/**
 * A booking or a cancellation the assistant offers. Nothing has happened yet: the app books
 * or cancels through the reservation endpoints when the user presses the card's button.
 * "book": resourceId, the times and the note are what to send. "cancel": reservationId, with
 * the rest to describe it.
 */
export interface AssistantProposal {
  kind: 'book' | 'cancel';
  resourceId: string | null;
  reservationId: string | null;
  resourceName: string;
  startDateTime: string;
  endDateTime: string;
  note: string | null;
}

/** `POST /api/assistant/messages`: the answer, and the cards it comes with. */
export interface AssistantReply {
  reply: string;
  proposals: AssistantProposal[];
}
