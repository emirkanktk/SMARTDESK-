using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;

namespace SmartDesk.API.Services;

// Sends a committed report to those who may manage its resource (ADR-0037): the Admins' group,
// and when the resource belongs to one team alone, that team's Manager, whose account id is the
// team's id (ADR-0022). Nobody else: a report is for those who fix things. Like the other
// notifiers, a failed push is logged and swallowed; the report is saved, and the list shows it
// on its next load.
public sealed class SignalRIssueNotifier(
    IHubContext<ReservationHub> hub,
    IApplicationDbContext db,
    ILogger<SignalRIssueNotifier> logger) : IIssueNotifier
{
    public Task IssueReportedAsync(IssueReportDto issue, CancellationToken cancellationToken) =>
        SendAsync(ReservationHub.IssueReported, issue, cancellationToken);

    public Task IssueUpdatedAsync(IssueReportDto issue, CancellationToken cancellationToken) =>
        SendAsync(ReservationHub.IssueUpdated, issue, cancellationToken);

    // `cancellationToken` is the caller's request token and is intentionally not used: the
    // report is committed, and a closed tab must not keep the others from hearing about it.
    private async Task SendAsync(string eventName, IssueReportDto issue, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await TrySendAsync(hub.Clients.Group(ReservationHub.AdminsGroup), eventName, issue);

        List<Guid> teams;
        try
        {
            teams = await db.ResourceTeams
                .AsNoTracking()
                .Where(t => t.ResourceId == issue.ResourceId)
                .Select(t => t.TeamId)
                .ToListAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to look up who manages the resource of issue {IssueId}", issue.Id);
            return;
        }

        if (teams.Count == 1)
        {
            await TrySendAsync(hub.Clients.User(teams[0].ToString()), eventName, issue);
        }
    }

    private async Task TrySendAsync(IClientProxy clients, string eventName, IssueReportDto issue)
    {
        try
        {
            await clients.SendAsync(eventName, issue, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to send {EventName} for issue {IssueId}", eventName, issue.Id);
        }
    }
}
