using SmartDesk.Application.Features.Approvals.Commands.LapseUnapprovedRequests;

namespace SmartDesk.API.Services;

// Lets go of the requests nobody approved before their start (ADR-0049): every
// Approvals:IntervalSeconds (30 by default) it runs LapseUnapprovedRequestsCommand in a fresh
// scope, so a request still waiting at its start goes at most one interval later. Set
// Approvals:Enabled to false to switch it off.
public sealed class ApprovalLapseWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ApprovalLapseWorker> logger)
    : PeriodicCommandWorker<LapseUnapprovedRequestsCommand>(scopeFactory, configuration, timeProvider, "Approvals")
{
    protected override void LogDone(int count) =>
        logger.LogInformation("{Count} request(s) lapsed without approval", count);

    protected override void LogFailed(Exception exception, TimeSpan retryIn) =>
        logger.LogWarning(exception, "Letting unapproved requests lapse failed; retrying in {Interval}", retryIn);
}
