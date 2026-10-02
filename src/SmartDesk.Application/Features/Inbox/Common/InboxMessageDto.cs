using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Inbox.Common;

// A message as the inbox shows it (ADR-0032), also the payload of the InboxMessageReceived
// event. The app writes the sentence from Kind and the meeting's details, in the reader's
// language.
public sealed record InboxMessageDto(
    Guid Id,
    Guid RecipientId,
    InboxMessageKind Kind,
    Guid? ReservationId,
    Guid? SenderId,
    string ResourceName,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note,
    string? Reason,
    DateTime CreatedAt,
    DateTime? ReadAt)
{
    // "Zeynep Kaya"; null when nobody sent it (a release) or the account is gone. Filled
    // afterwards, like ReservationDto.UserDisplayName: accounts live in Identity.
    public string? SenderName { get; init; }

    public static readonly Expression<Func<InboxMessage, InboxMessageDto>> Projection = m => new InboxMessageDto(
        m.Id, m.RecipientId, m.Kind, m.ReservationId, m.SenderId, m.ResourceName, m.StartDateTime, m.EndDateTime, m.Note, m.Reason, m.CreatedAt, m.ReadAt);

    // For messages just written, compiled once (after Projection, which it needs).
    private static readonly Func<InboxMessage, InboxMessageDto> Map = Projection.Compile();

    public static InboxMessageDto From(InboxMessage m) => Map(m);
}
