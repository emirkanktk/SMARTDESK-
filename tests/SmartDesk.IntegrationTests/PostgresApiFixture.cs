using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

        var options = new DbContextOptionsBuilder<SmartDeskDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
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
        builder.UseEnvironment("Development");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "postgres-api";
}
