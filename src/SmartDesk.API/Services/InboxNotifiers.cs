using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.API.Services;

// Each new inbox message to its recipient's open screens over SignalR and to their phones and
// browsers (ADR-0056). Neither throws (IInboxNotifier).
public sealed class InboxNotifiers(SignalRInboxNotifier live, PushInboxNotifier push) : IInboxNotifier
{
    public async Task MessagesSentAsync(IReadOnlyList<InboxMessageDto> messages, CancellationToken cancellationToken)
    {
        await live.MessagesSentAsync(messages, cancellationToken);
        await push.MessagesSentAsync(messages, cancellationToken);
    }
}
