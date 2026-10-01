using SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;

namespace SmartDesk.API.Services;

// Sends reservation reminders (ADR-0018): every Reminders:IntervalSeconds (30 by default) it
// runs SendDueRemindersCommand in a fresh scope. A reminder can therefore arrive up to one
// interval late, never early. Set Reminders:Enabled to false to switch it off.
public sealed class ReservationReminderWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ReservationReminderWorker> logger)
    : PeriodicCommandWorker<SendDueRemindersCommand>(scopeFactory, configuration, timeProvider, "Reminders")
{
    protected override void LogDone(int count) =>
        logger.LogInformation("Sent {Count} reservation reminder(s)", count);

    protected override void LogFailed(Exception exception, TimeSpan retryIn) =>
        logger.LogWarning(exception, "Sending reservation reminders failed; retrying in {Interval}", retryIn);
}
