using SmartDesk.Application.Features.Inbox.Common;

namespace SmartDesk.Application.Common.Interfaces;

// Tells people, live, that a message reached their inbox (ADR-0032), so the count in the menu
// goes up and a toast says what it is. Called only after the messages are committed. Like
// IReservationNotifier, implementations must not throw: the messages are saved either way, and
// the inbox shows them on its next load.
public interface IInboxNotifier
{
    Task MessagesSentAsync(IReadOnlyList<InboxMessageDto> messages, CancellationToken cancellationToken);
}
