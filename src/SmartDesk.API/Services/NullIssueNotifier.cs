using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;

namespace SmartDesk.API.Services;

// Like NullReservationNotifier: registered with TryAdd, so the SignalR notifier registered
// before it wins, and it only applies where no hub is wired up.
public sealed class NullIssueNotifier : IIssueNotifier
{
    public Task IssueReportedAsync(IssueReportDto issue, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task IssueUpdatedAsync(IssueReportDto issue, CancellationToken cancellationToken) => Task.CompletedTask;
}
