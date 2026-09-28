using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartDesk.Infrastructure.Persistence;

// "PostgreSQL answers" isn't enough for "the API works": on an empty or outdated database the
// connection succeeds but every query fails. Reporting pending migrations as Unhealthy makes
// GET /health - and any container health check built on it - mean "usable".
public sealed class PendingMigrationsHealthCheck(SmartDeskDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        return pending.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s): {string.Join(", ", pending)}");
    }
}
