using SmartDesk.Application.Features.Issues.Common;

namespace SmartDesk.Application.Common.Interfaces;

// Tells those who look after a resource, live, that a problem was reported there or that a
// report moved on (ADR-0037): the Admins, and the Manager of the team it belongs to alone, the
// same people who may manage it. Called only after the change is committed. Like
// IReservationNotifier, implementations must not throw: the report is saved either way.
public interface IIssueNotifier
{
    Task IssueReportedAsync(IssueReportDto issue, CancellationToken cancellationToken);

    Task IssueUpdatedAsync(IssueReportDto issue, CancellationToken cancellationToken);
}
