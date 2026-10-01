using SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;

namespace SmartDesk.API.Services;

// Releases bookings nobody checked in to (ADR-0030): every CheckIn:IntervalSeconds (30 by
// default) it runs ReleaseNoShowsCommand in a fresh scope, so a booking goes at most one
// interval after its check-in closed, never before. Set CheckIn:Enabled to false to switch
// releasing off; checking in still works.
public sealed class NoShowReleaseWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<NoShowReleaseWorker> logger)
    : PeriodicCommandWorker<ReleaseNoShowsCommand>(scopeFactory, configuration, timeProvider, "CheckIn")
{
    protected override void LogDone(int count) =>
        logger.LogInformation("Released {Count} booking(s) nobody checked in to", count);

    protected override void LogFailed(Exception exception, TimeSpan retryIn) =>
        logger.LogWarning(exception, "Releasing bookings nobody checked in to failed; retrying in {Interval}", retryIn);
}
