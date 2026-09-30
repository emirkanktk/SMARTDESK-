using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.Tests.TestSupport;

// The real SmartDeskDbContext on an in-memory SQLite database: handlers run their actual LINQ
// through a relational provider (joins, subqueries, FK checks), unlike EF's InMemory provider.
// Seeded resource types come from the model's HasData, same as the real migration.
// Postgres-specific behaviour (e.g. constraints added in raw SQL migrations) is not covered.
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    // A fresh context per call, so a test's assertions read from the database rather than
    // from entities the handler left tracked in its own context. Collections load one query
    // each, as in production (AddInfrastructure). Interceptors let a test step in between a
    // handler's commands, e.g. to stage a race.
    public SmartDeskDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<SmartDeskDbContext>()
            .UseSqlite(_connection, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(interceptors)
            .Options);

    public async Task<Resource> AddResourceAsync(
        string name = "Meeting Room A",
        Guid? typeId = null,
        int capacity = 8,
        ResourceStatus status = ResourceStatus.Active,
        params string[] features)
    {
        await using var context = CreateContext();
        var resource = new Resource
        {
            Name = name,
            ResourceTypeId = typeId ?? ResourceTypeSeedData.MeetingRoomId,
            Capacity = capacity,
            Location = "2nd Floor",
            Status = status,
            Features = features.Select(f => new ResourceFeature { Name = f }).ToList(),
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource;
    }

    public async Task<Reservation> AddReservationAsync(
        Guid resourceId,
        DateTime start,
        DateTime end,
        ReservationStatus status = ReservationStatus.Confirmed,
        Guid? userId = null,
        int? reminderMinutes = null,
        DateTime? reminderSentAt = null)
    {
        await using var context = CreateContext();
        var reservation = new Reservation
        {
            ResourceId = resourceId,
            UserId = userId ?? Guid.NewGuid(),
            StartDateTime = start,
            EndDateTime = end,
            Status = status,
            ReminderMinutes = reminderMinutes,
            ReminderSentAt = reminderSentAt,
        };
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();
        return reservation;
    }

    public async Task<ResourceBlock> AddBlockAsync(Guid resourceId, DateTime start, DateTime end, string reason = "Maintenance")
    {
        await using var context = CreateContext();
        var block = new ResourceBlock
        {
            ResourceId = resourceId,
            StartDateTime = start,
            EndDateTime = end,
            Reason = reason,
            CreatedByUserId = Guid.NewGuid(),
        };
        context.ResourceBlocks.Add(block);
        await context.SaveChangesAsync();
        return block;
    }

    public void Dispose() => _connection.Dispose();
}
