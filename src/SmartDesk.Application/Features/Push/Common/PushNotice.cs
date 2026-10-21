using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Push.Common;

// What to tell whom on their devices (ADR-0056), not yet in words: the sender writes it for each
// device in its owner's language, with the times in the office's zone, as it goes out.
public sealed record PushNotice(PushTopic Topic, IReadOnlyCollection<Guid> RecipientIds)
{
    // The booking a reminder, a warning, a release or an ending-soon notice is about.
    public ReservationDto? Reservation { get; init; }

    // The message an InboxMessage notice repeats.
    public InboxMessageDto? Message { get; init; }
}

public enum PushTopic
{
    // Before a booking starts, as its reminder asks (ADR-0018).
    StartingSoon,

    // Nobody has checked in yet, and the booking is released soon unless someone does (ADR-0034).
    ReleaseWarning,

    // The booking was released because nobody checked in (ADR-0030).
    Released,

    // The booking ends soon and can run longer (ADR-0033).
    EndingSoon,

    // A new message in the inbox (ADR-0032).
    InboxMessage,

    // The one someone asks for, to see notifications arrive.
    Test,
}
