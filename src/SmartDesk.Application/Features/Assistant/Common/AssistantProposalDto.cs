namespace SmartDesk.Application.Features.Assistant.Common;

// A booking or a cancellation the assistant offers (ADR-0029). The app shows it as a card, and
// only the user's click does it - through POST /api/reservations or
// POST /api/reservations/{id}/cancel, with every rule those apply.
// Kind "book": ResourceId, the times and the note are what to send. Kind "cancel":
// ReservationId, with the rest to describe it.
public sealed record AssistantProposalDto(
    string Kind,
    Guid? ResourceId,
    Guid? ReservationId,
    string ResourceName,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note)
{
    public const string Book = "book";
    public const string Cancel = "cancel";
}
