using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SmartDeskDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SmartDeskDbContext>());

        // Backs GET /health: "Healthy" only if PostgreSQL answers and the schema is up to date,
        // so a container orchestrator or uptime check can tell "process up" from "service usable".
        services.AddHealthChecks()
            .AddDbContextCheck<SmartDeskDbContext>("database")
            .AddCheck<PendingMigrationsHealthCheck>("migrations");

        // AddIdentityCore rather than AddIdentity: this is a bearer-token API, and AddIdentity
        // also registers cookie authentication, which turns a 403 into a 302 redirect to a
        // login page that doesn't exist.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer and Jwt:Audience must be set.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} bytes. Set it with: " +
                "dotnet user-secrets set \"Jwt:Key\" \"<a long random string>\" --project src/SmartDesk.API")
            .Validate(o => o.AccessTokenLifetimeMinutes > 0, "Jwt:AccessTokenLifetimeMinutes must be positive.")
            .ValidateOnStart();

        services.AddOptions<BootstrapAdminOptions>()
            .Bind(configuration.GetSection(BootstrapAdminOptions.SectionName))
            .Validate(o => o.IsComplete, "BootstrapAdmin:Email and BootstrapAdmin:Password must be set together.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IdentityDataSeeder>();
        services.AddScoped<BootstrapAdminSeeder>();
        services.AddScoped<DemoResourceSeeder>();

        return services;
    }

    // Applies pending migrations. Only runs when Database:MigrateOnStartup is on (see
    // Program.cs): for a throwaway container database that's convenient, but migrating a shared
    // or production database should stay a deliberate step (`dotnet ef database update`).
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DependencyInjection));

        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await dbContext.Database.MigrateAsync();
    }

    // Roles in every environment; demo users and demo resources only when includeDemoData
    // (Development - ADR-0007); the configured bootstrap admin in any environment (ADR-0016).
    // Resource types come from the migration, not from here.
    public static async Task SeedDatabaseAsync(this IServiceProvider services, bool includeDemoData)
    {
        await using var scope = services.CreateAsyncScope();

        var identitySeeded = await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync(includeDemoData);
        if (identitySeeded)
        {
            // After the demo users, so its "no admin yet" warning never fires in Development.
            await scope.ServiceProvider.GetRequiredService<BootstrapAdminSeeder>().SeedAsync();
        }

        if (identitySeeded && includeDemoData)
        {
            await scope.ServiceProvider.GetRequiredService<DemoResourceSeeder>().SeedAsync();
        }
    }
}
