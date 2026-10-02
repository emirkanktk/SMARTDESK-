using MediatR;

namespace SmartDesk.API.Services;

// Runs a command in a fresh scope every "{section}:IntervalSeconds" (30 by default), for the
// jobs that act on the clock. A job can therefore act up to one interval late, never early.
// Set "{section}:Enabled" to false to switch it off.
public abstract class PeriodicCommandWorker<TCommand>(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    string section) : BackgroundService
    where TCommand : IRequest<int>, new()
{
    // What a run that did something (count > 0), or failed, writes to the log.
    protected abstract void LogDone(int count);

    protected abstract void LogFailed(Exception exception, TimeSpan retryIn);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue($"{section}:Enabled", true))
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue($"{section}:IntervalSeconds", 30)));
        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new TCommand(), stoppingToken);
                if (count > 0)
                {
                    LogDone(count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // E.g. the database isn't reachable or migrated yet. Try again next tick rather
                // than letting one failure stop the job for good.
                LogFailed(exception, interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
