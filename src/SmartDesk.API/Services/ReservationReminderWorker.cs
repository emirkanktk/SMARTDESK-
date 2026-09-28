using MediatR;
using SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;

namespace SmartDesk.API.Services;

// Sends reservation reminders (ADR-0018): every Reminders:IntervalSeconds (30 by default) it
// runs SendDueRemindersCommand in a fresh scope. A reminder can therefore arrive up to one
// interval late, never early. Set Reminders:Enabled to false to switch it off.
public sealed class ReservationReminderWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<ReservationReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Reminders:Enabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Reminders:IntervalSeconds", 30)));
        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new SendDueRemindersCommand(), stoppingToken);
                if (sent > 0)
                {
                    logger.LogInformation("Sent {Count} reservation reminder(s)", sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // E.g. the database isn't reachable or migrated yet. Try again next tick rather
                // than letting one failure stop reminders for good.
                logger.LogWarning(exception, "Sending reservation reminders failed; retrying in {Interval}", interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
