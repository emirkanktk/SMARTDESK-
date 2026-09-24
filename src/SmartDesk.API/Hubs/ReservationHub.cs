using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SmartDesk.API.Hubs;

// Event names are the contract in docs/API_CONTRACTS.md. The hub itself has no methods:
// the server pushes, clients only listen.
[Authorize]
public class ReservationHub : Hub
{
    public const string Created = "ReservationCreated";
    public const string Updated = "ReservationUpdated";
    public const string Cancelled = "ReservationCancelled";
}
