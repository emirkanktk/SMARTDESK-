using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.API.Services;

// Sends a committed reservation change to everyone who may see that resource, and reminders
// to their owner. A shared resource's events go to every client. A team resource's go only to
// its teams, the Admins, and the reservation's owner (ADR-0022): the owner keeps seeing their
// own reservation even after leaving the team, so they must hear when it's moved or cancelled.
// The owner may get a team-resource event twice; clients upsert or remove by id. A failed push
// is logged and swallowed: the row is already saved, so the HTTP call (or the reminder job)
// must still succeed.
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
        SendAsync(OwnerOf(reservation), ReservationHub.StartingSoon, reservation);

    public Task ReservationReleasedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        SendAsync(OwnerOf(reservation), ReservationHub.Released, reservation);

    public Task ReservationReleaseWarningAsync(
        ReservationDto reservation, IReadOnlyCollection<Guid> recipients, CancellationToken cancellationToken) =>
        SendAsync(hub.Clients.Users(recipients.Distinct().Select(id => id.ToString()).ToList()), ReservationHub.ReleaseWarning, reservation);

    public Task ReservationEndingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        SendAsync(OwnerOf(reservation), ReservationHub.EndingSoon, reservation);

    // `cancellationToken` is the caller's request token and is intentionally not used: the row
    // is already committed, and aborting the HTTP request must not cancel the push that tells
    // everyone else.
    private async Task BroadcastAsync(string eventName, ReservationDto reservation, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        List<Guid> teams;
        try
        {
            teams = await db.ResourceTeams
                .AsNoTracking()
                .Where(t => t.ResourceId == reservation.ResourceId)
                .Select(t => t.TeamId)
                .ToListAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to look up who may see reservation {ReservationId}", reservation.Id);
            return;
        }

        if (teams.Count == 0)
        {
            await SendAsync(hub.Clients.All, eventName, reservation);
            return;
        }

        // A proxy can't mix groups and a user, hence two sends.
        await SendAsync(
            hub.Clients.Groups([ReservationHub.AdminsGroup, .. teams.Select(ReservationHub.TeamGroup)]),
            eventName,
            reservation);
        await SendAsync(OwnerOf(reservation), eventName, reservation);
    }

    private IClientProxy OwnerOf(ReservationDto reservation) => hub.Clients.User(reservation.UserId.ToString());

    private async Task SendAsync(IClientProxy clients, string eventName, ReservationDto reservation)
    {
        try
        {
            await clients.SendAsync(eventName, reservation, CancellationToken.None);
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
