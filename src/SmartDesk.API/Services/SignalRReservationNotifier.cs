using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.API.Services;

// Sends a committed reservation change to everyone who may see that resource, and reminders
// to their owner. A shared resource's events go to every client; a team resource's only to its
// teams and the Admins (ADR-0022), so its schedule doesn't reach other teams. A failed push is
// logged and swallowed: the row is already saved, so the HTTP call (or the reminder job) must
// still succeed.
public sealed class SignalRReservationNotifier(
    IHubContext<ReservationHub> hub,
    IApplicationDbContext db,
    ILogger<SignalRReservationNotifier> logger) : IReservationNotifier
{
    public Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Created, reservation, cancellationToken);

    public Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Updated, reservation, cancellationToken);

    public Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        BroadcastAsync(ReservationHub.Cancelled, reservation, cancellationToken);

    // Only the owner's connections: SignalR's default user id is the token's subject, the same
    // user id the reservation stores.
    public Task ReservationStartingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        SendAsync(() => Task.FromResult(hub.Clients.User(reservation.UserId.ToString())), ReservationHub.StartingSoon, reservation);

    private Task BroadcastAsync(string eventName, ReservationDto reservation, CancellationToken cancellationToken)
    {
        // `cancellationToken` is the caller's request token and is intentionally not used:
        // the row is already committed, and aborting the HTTP request must not cancel the push
        // that tells everyone else.
        _ = cancellationToken;
        return SendAsync(() => AudienceAsync(reservation.ResourceId), eventName, reservation);
    }

    private async Task<IClientProxy> AudienceAsync(Guid resourceId)
    {
        var teams = await db.ResourceTeams
            .AsNoTracking()
            .Where(t => t.ResourceId == resourceId)
            .Select(t => t.TeamId)
            .ToListAsync(CancellationToken.None);

        return teams.Count == 0
            ? hub.Clients.All
            : hub.Clients.Groups([ReservationHub.AdminsGroup, .. teams.Select(ReservationHub.TeamGroup)]);
    }

    private async Task SendAsync(Func<Task<IClientProxy>> clients, string eventName, ReservationDto reservation)
    {
        try
        {
            await (await clients()).SendAsync(eventName, reservation, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to send {EventName} for reservation {ReservationId}",
                eventName,
                reservation.Id);
        }
    }
}
