using Microsoft.AspNetCore.SignalR;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;

namespace SmartDesk.API.Services;

// Sends each new inbox message to its recipient's open connections, and nobody else's
// (ADR-0032). SignalR's user id is the token's subject, the account id messages are kept by.
// Like the reservation notifier, a failed push is logged and swallowed: the message is saved,
// and the inbox shows it on its next load.
public sealed class SignalRInboxNotifier(IHubContext<ReservationHub> hub, ILogger<SignalRInboxNotifier> logger) : IInboxNotifier
{
    // `cancellationToken` is the caller's request token and is intentionally not used: the
    // messages are committed, and a closed tab must not stop the others being told.
    public async Task MessagesSentAsync(IReadOnlyList<InboxMessageDto> messages, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        foreach (var message in messages)
        {
            try
            {
                await hub.Clients.User(RecipientOf(message)).SendAsync(ReservationHub.InboxMessageReceived, message, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to send inbox message {MessageId}", message.Id);
            }
        }
    }

    private static string RecipientOf(InboxMessageDto message) => message.RecipientId.ToString();
}
