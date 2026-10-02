using SmartDesk.Application.Features.Reservations.Commands.SendEndingSoonNotices;

namespace SmartDesk.API.Services;

// Asks owners whether a booking that's about to end needs longer (ADR-0033): every
// EndingSoon:IntervalSeconds (30 by default) it runs SendEndingSoonNoticesCommand in a fresh
// scope, so the question comes up to one interval late, never early. Set EndingSoon:Enabled to
// false to switch it off; extending still works.
public sealed class EndingSoonNoticeWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<EndingSoonNoticeWorker> logger)
    : PeriodicCommandWorker<SendEndingSoonNoticesCommand>(scopeFactory, configuration, timeProvider, "EndingSoon")
{
    protected override void LogDone(int count) =>
        logger.LogInformation("Asked about {Count} booking(s) ending soon", count);

    protected override void LogFailed(Exception exception, TimeSpan retryIn) =>
        logger.LogWarning(exception, "Asking about bookings ending soon failed; retrying in {Interval}", retryIn);
}
