using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartDesk.Application.Common.Security;
using SmartDesk.Domain.Constants;

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

    // Also only to the owner: their booking was released because nobody checked in (ADR-0030).
    // Everyone else hears ReservationCancelled, which frees the slot on their screens.
    public const string Released = "ReservationReleased";

    // To the owner and those invited: nobody has checked in yet, and it's released in five
    // minutes (ADR-0034).
    public const string ReleaseWarning = "ReservationReleaseWarning";

    // To the owner only: their booking is about to end; do they need longer (ADR-0033)?
    public const string EndingSoon = "ReservationEndingSoon";

    // A message reached the user's inbox (ADR-0032); sent to that user only, with the message.
    public const string InboxMessageReceived = "InboxMessageReceived";

    // Events about a team's resource go to its teams and the Admins only (ADR-0022).
    public const string AdminsGroup = "admins";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public override async Task OnConnectedAsync()
    {
        if (UserId is { } userId)
        {
            connections.Add(userId, Context);
        }

        // From the account as it is now (JwtBearerSetup), so a team change takes effect on the
        // reconnect that the change itself forces.
        if (Context.User?.IsInRole(Roles.Admin) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        if (Guid.TryParse(Context.User?.FindFirstValue(SmartDeskClaims.Team), out var teamId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
        }

        await base.OnConnectedAsync();
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
