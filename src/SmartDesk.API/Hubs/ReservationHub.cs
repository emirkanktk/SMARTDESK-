using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartDesk.API.Hubs;

// Event names are the contract in docs/API_CONTRACTS.md. The hub itself has no methods:
// the server pushes, clients only listen.
[Authorize]
public class ReservationHub(HubConnectionRegistry connections) : Hub
{
    public const string Created = "ReservationCreated";
    public const string Updated = "ReservationUpdated";
    public const string Cancelled = "ReservationCancelled";

    // Sent only to the reservation's owner, not to everyone (ADR-0018).
    public const string StartingSoon = "ReservationStartingSoon";

    public override Task OnConnectedAsync()
    {
        if (UserId is { } userId)
        {
            connections.Add(userId, Context);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (UserId is { } userId)
        {
            connections.Remove(userId, Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    private Guid? UserId =>
        Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
