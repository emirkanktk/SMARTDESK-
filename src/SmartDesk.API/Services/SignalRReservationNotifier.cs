using Microsoft.AspNetCore.SignalR;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.API.Services;

// Broadcasts a committed reservation change to every connected client. A failed push is
// logged and swallowed: the row is already saved, so the HTTP call must still succeed.
public sealed class SignalRReservationNotifier(
    IHubContext<ReservationHub> hub,
    ILogger<SignalRReservationNotifier> logger) : IReservationNotifier
{
    public Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Created, reservation, cancellationToken);

    public Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Updated, reservation, cancellationToken);

    public Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Cancelled, reservation, cancellationToken);

    // `cancellationToken` is the caller's request token and is intentionally not passed to
    // SendAsync: the row is already committed, and aborting the HTTP request must not
    // cancel the broadcast that tells everyone else.
    private async Task BroadcastAsync(
        string eventName, ReservationDto reservation, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        try
        {
            await hub.Clients.All.SendAsync(eventName, reservation, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to broadcast {EventName} for reservation {ReservationId}",
                eventName,
                reservation.Id);
        }
    }
}
