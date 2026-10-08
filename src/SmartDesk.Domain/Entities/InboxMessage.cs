using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// A message in someone's inbox (ADR-0032). It keeps what the meeting looked like when it was
// sent - the room, the times, the organizer's note - so it reads the same later, whatever
// happens to the booking. The app writes the words, in the reader's language. Since ADR-0037
// it also tells people a problem they reported is fixed.
public class InboxMessage
{
    public Guid Id { get; set; }
    public Guid RecipientId { get; set; }
    public InboxMessageKind Kind { get; set; }

    // Who caused it: the organizer, or an Admin changing their booking. Null when nobody did,
    // e.g. a booking released because nobody checked in.
    public Guid? SenderId { get; set; }

    // The booking it's about, for a link. Null once that's gone.
    public Guid? ReservationId { get; set; }

    public string ResourceName { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string? Note { get; set; }

    // Why someone can't come, in a MeetingDeclined or SeriesDeclined message to the organizer.
    public string? Reason { get; set; }

    // The repeating meeting a Series* message is about (ADR-0035): its rule, which never
    // changes, says which dates. The times above are then its first date the message covers.
    public Guid? SeriesId { get; set; }

    public ReservationSeries? Series { get; set; }

    // What the problem an IssueResolved message is about was (ADR-0037); null for the rest.
    public IssueCategory? IssueCategory { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
