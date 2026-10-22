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
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure.Assistant;
using SmartDesk.Infrastructure.Calendar;
using SmartDesk.Infrastructure.Email;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Auditing;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Infrastructure.Persistence.Seed.DemoData;
using SmartDesk.Infrastructure.Push;
using SmartDesk.Infrastructure.Reports;

namespace SmartDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A query that loads collections sends one statement per collection. In one statement,
        // two collections come back multiplied: a resource with 20 features and 20 teams would
        // be 400 rows.
        // Every save notes the times it frees for the waiting list (ADR-0043), and writes the
        // history of what it changes (ADR-0052).
        services.AddScoped<FreedSlotsInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<SmartDeskDbContext>((provider, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .AddInterceptors(
                    provider.GetRequiredService<FreedSlotsInterceptor>(),
                    provider.GetRequiredService<AuditInterceptor>()));
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
                // Version 3 keeps passkeys (ADR-0057).
                options.Stores.SchemaVersion = SmartDeskDbContext.IdentitySchemaVersion;
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

        services.AddOptions<CheckInOptions>()
            .Bind(configuration.GetSection(CheckInOptions.SectionName))
            .Validate(o => o.HasValidGrace,
                $"CheckIn:GraceMinutes must be from {CheckInRules.MinGrace.TotalMinutes} to {CheckInRules.MaxGrace.TotalMinutes}.")
            .ValidateOnStart();
        services.AddSingleton<ICheckInTimes, CheckInTimes>();

        // The usage report as an Excel workbook (ADR-0045).
        services.AddSingleton<IUsageReportWorkbook, UsageReportWorkbook>();

        // Bookings as iCalendar files and feeds (ADR-0048).
        services.AddSingleton<ICalendarWriter, IcalCalendarWriter>();

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

        // Notifications on phones and browsers (ADR-0056): handlers and notifiers queue, a background
        // worker encrypts and sends. Off without the VAPID keys; with them, they must be a pair, and
        // the push services need a way to reach whoever runs the server.
        services.AddOptions<WebPushOptions>()
            .Bind(configuration.GetSection(WebPushOptions.SectionName))
            .Validate(o => !o.IsConfigured || VapidKeys.AreValid(o.PublicKey, o.PrivateKey),
                "WebPush:PublicKey and WebPush:PrivateKey must be a P-256 key pair, base64url. Make one with: " +
                "dotnet run --project src/SmartDesk.API -- --vapid-keys")
            .Validate(o => !o.IsConfigured || o.HasValidSubject,
                "WebPush:Subject must be a mailto: address or an https: page, not on localhost or .local, e.g. mailto:it@example.com.")
            .ValidateOnStart();
        services.AddSingleton<PushQueue>();
        services.AddSingleton<IPushSender>(sp => sp.GetRequiredService<PushQueue>());
        services.AddHttpClient(PushDispatchWorker.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHostedService<PushDispatchWorker>();

        // Reset links expire, and work once: a reset changes the security stamp they're tied to.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = AccountEmails.ResetLinkLifetime);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddMemoryCache();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IUserAccessChecker, UserAccessChecker>();
        services.AddScoped<IUserSessions, UserSessions>();
        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.SectionName))
            .Validate(o => o.IdleDays > 0 && o.MaxDays >= o.IdleDays && o.ReuseGraceSeconds >= 0,
                "Sessions: IdleDays must be positive, MaxDays at least IdleDays, and ReuseGraceSeconds not negative.")
            .ValidateOnStart();
        services.AddScoped<UserAccessCache>();
        // Passkeys (ADR-0057): checked by Identity, on the addresses the app is served at.
        services.AddSingleton(sp => PasskeyOrigins.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<PasskeyChallengeLog>();
        services.AddScoped<IPasskeys, Passkeys>();
        // The API registers the SignalR one first; this is the fallback without a hub.
        services.TryAddSingleton<IRealtimeSessions, NoRealtimeSessions>();
        services.AddScoped<IdentityDataSeeder>();
        services.AddScoped<BootstrapAdminSeeder>();
        services.AddScoped<DemoResourceSeeder>();
        services.AddScoped<DemoDataSeeder>();

        // The assistant (ADR-0029): Claude through the official SDK, or Gemini over its REST API
        // (ADR-0036), as Assistant:Provider says. Without Assistant:ApiKey it's off, and nothing
        // else notices.
        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .Validate(o => o.MaxTokens > 0 && o.MaxToolRounds > 0 && o.TimeoutSeconds > 0,
                "Assistant:MaxTokens, Assistant:MaxToolRounds and Assistant:TimeoutSeconds must be positive.")
            .Validate(o => o.HasKnownProvider, "Assistant:Provider must be Anthropic or Gemini.")
            .ValidateOnStart();
        services.AddHttpClient(GeminiAssistantModel.HttpClientName);
        // Claude's SDK keeps its own HTTP client, as before.
        services.AddSingleton(sp => new ClaudeAssistantModel(
            sp.GetRequiredService<IOptions<AssistantOptions>>(),
            sp.GetRequiredService<ILogger<ClaudeAssistantModel>>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<GeminiAssistantModel>();
        services.AddSingleton<IAssistantModel>(sp => sp.GetRequiredService<IOptions<AssistantOptions>>().Value.UsesGemini
            ? sp.GetRequiredService<GeminiAssistantModel>()
            : sp.GetRequiredService<ClaudeAssistantModel>());

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

    // A lived-in office for a demo, on `--demo-data` (DemoDataSeeder). After SeedDatabaseAsync,
    // whose demo users and resources it builds on.
    // False when it wrote nothing, and the log says why.
    public static async Task<bool> SeedDemoDataAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
        if ((await dbContext.Database.GetPendingMigrationsAsync()).Any())
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DependencyInjection)).LogError(
                "Demo data skipped: the database isn't up to date. Apply the migrations first (dotnet ef database update).");
            return false;
        }

        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }
}
