using System.Linq.Expressions;
using SmartDesk.Application.Features.Reservations.Common;
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

    // The rule of the repeating meeting a Series* message is about (ADR-0035): with the times,
    // which are its first date the message covers, it says "every Monday, 10:00-11:00, until
    // 30 Nov".
    public ReservationSeriesDto? Series { get; init; }

    public static readonly Expression<Func<InboxMessage, InboxMessageDto>> Projection = m => new InboxMessageDto(
        m.Id, m.RecipientId, m.Kind, m.ReservationId, m.SenderId, m.ResourceName, m.StartDateTime, m.EndDateTime, m.Note, m.Reason, m.CreatedAt, m.ReadAt)
    {
        Series = m.Series == null
            ? null
            : new ReservationSeriesDto(m.Series.Id, m.Series.Frequency, m.Series.Interval, m.Series.DaysOfWeek, m.Series.Until, m.Series.TimeZone),
    };

    // For messages just written, compiled once (after Projection, which it needs).
    private static readonly Func<InboxMessage, InboxMessageDto> Map = Projection.Compile();

    public static InboxMessageDto From(InboxMessage m) => Map(m);
}
