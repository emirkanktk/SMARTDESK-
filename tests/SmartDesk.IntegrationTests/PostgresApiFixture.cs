using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SmartDesk.IntegrationTests;

// One PostgreSQL for the collection. The schema is migrated before the host starts,
// because startup seeding refuses to run while migrations are pending.
public sealed class PostgresApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // With the Identity schema version the app runs on, which Identity reads from its options.
        var identity = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = SmartDeskDbContext.IdentitySchemaVersion)
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<SmartDeskDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseApplicationServiceProvider(identity)
            .Options;
        await using var db = new SmartDeskDbContext(options);
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Key", new string('k', 32));
        // The suite signs in far more often in a minute than any person: the limit itself is
        // tested on an app of its own (ForwardedHeadersTests).
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
        // Development reads the developer's user secrets: an assistant key there must not turn
        // the assistant on in tests that expect it off. Tests that want one set their own.
        builder.UseSetting("Assistant:ApiKey", string.Empty);
        // Likewise notifications (ADR-0056): off here, whatever keys the developer has; PushTests
        // turns them on for a host of its own.
        builder.UseSetting("WebPush:PublicKey", string.Empty);
        builder.UseSetting("WebPush:PrivateKey", string.Empty);
        // So a test can wait for a reminder the real job sends, instead of up to 30 seconds.
        builder.UseSetting("Reminders:IntervalSeconds", "1");
        // The tests' logs stay out of a developer's own Seq (ADR-0053).
        builder.UseSetting("Seq:ServerUrl", string.Empty);
        builder.UseEnvironment("Development");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "postgres-api";
}
