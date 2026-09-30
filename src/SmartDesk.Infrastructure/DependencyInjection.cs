using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Infrastructure.Email;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A query that loads collections sends one statement per collection. In one statement,
        // two collections come back multiplied: a resource with 20 features and 20 teams would
        // be 400 rows.
        services.AddDbContext<SmartDeskDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
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
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
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

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .Validate(o => o.HasValidFrontendUrl,
                "App:FrontendUrl must be the absolute http(s) address the frontend is served on, e.g. http://localhost:4200.")
            .ValidateOnStart();
        services.AddSingleton<IFrontendLinks, FrontendLinks>();

        // Email (ADR-0019): handlers queue, a background worker sends - over SMTP when
        // Email:Smtp:Host is set, to the log otherwise. The log gets the whole email, reset
        // links included, so that fallback is for Development only: anywhere else, anyone who
        // can read the logs could take over any account. There, no SMTP host means no start.
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => MailboxAddress.TryParse(o.From, out _), "Email:From must be an email address, e.g. SmartDesk <no-reply@example.com>.")
            .Validate<IHostEnvironment>(
                (o, environment) => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.Smtp.Host),
                "Email:Smtp:Host is required outside Development; without it every email, password reset links included, " +
                "would only be written to the log. Set Email__Smtp__Host, Email__Smtp__Port and Email__From.")
            .ValidateOnStart();
        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<EmailQueue>());
        services.AddSingleton<IEmailTransport>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Smtp.Host)
                ? ActivatorUtilities.CreateInstance<LoggingEmailTransport>(sp)
                : ActivatorUtilities.CreateInstance<SmtpEmailTransport>(sp));
        services.AddHostedService<EmailDispatchWorker>();

        // Reset links expire, and work once: a reset changes the security stamp they're tied to.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = AccountEmails.ResetLinkLifetime);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddMemoryCache();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IUserAccessChecker, UserAccessChecker>();
        services.AddScoped<UserAccessCache>();
        // The API registers the SignalR one first; this is the fallback without a hub.
        services.TryAddSingleton<IRealtimeSessions, NoRealtimeSessions>();
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
