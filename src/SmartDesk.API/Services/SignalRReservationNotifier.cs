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

    private async Task BroadcastAsync(string eventName, ReservationDto reservation, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.All.SendAsync(eventName, reservation, cancellationToken);
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
