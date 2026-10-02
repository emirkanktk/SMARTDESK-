using SmartDesk.Application.Features.CheckIn.Commands.WarnBeforeRelease;

namespace SmartDesk.API.Services;

// Warns the owner and those invited five minutes before a booking nobody has checked in to is
// released (ADR-0034). Same schedule and switch as the release itself: CheckIn:IntervalSeconds
// and CheckIn:Enabled.
public sealed class ReleaseWarningWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ReleaseWarningWorker> logger)
    : PeriodicCommandWorker<WarnBeforeReleaseCommand>(scopeFactory, configuration, timeProvider, "CheckIn")
{
    protected override void LogDone(int count) =>
        logger.LogInformation("Warned about {Count} booking(s) nobody has checked in to yet", count);

    protected override void LogFailed(Exception exception, TimeSpan retryIn) =>
        logger.LogWarning(exception, "Warning about bookings nobody has checked in to failed; retrying in {Interval}", retryIn);
}
