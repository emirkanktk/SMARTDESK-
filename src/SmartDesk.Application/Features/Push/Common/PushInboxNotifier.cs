using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;

namespace SmartDesk.Application.Features.Push.Common;

// Puts every new inbox message (ADR-0032) on its recipient's devices as well (ADR-0056): an
// invitation, an answer, an approval, a time from the waiting list. The inbox keeps it either way.
public sealed class PushInboxNotifier(IPushSender push) : IInboxNotifier
{
    public Task MessagesSentAsync(IReadOnlyList<InboxMessageDto> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            push.Queue(new PushNotice(PushTopic.InboxMessage, [message.RecipientId]) { Message = message });
        }

        return Task.CompletedTask;
    }
}
