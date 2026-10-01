using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;

namespace SmartDesk.API.Services;

// Like NullReservationNotifier: registered with TryAdd, so the SignalR notifier registered
// before it wins, and it only applies where no hub is wired up.
public sealed class NullInboxNotifier : IInboxNotifier
{
    public Task MessagesSentAsync(IReadOnlyList<InboxMessageDto> messages, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
