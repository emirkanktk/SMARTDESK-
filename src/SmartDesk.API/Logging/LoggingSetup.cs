using System.Globalization;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.API.Logging;

// Structured logs (ADR-0053). Serilog writes what every ILogger says and keeps each value in a
// message - {ReservationId}, {Count} - as a field of its own: to the console, and to Seq when
// Seq:ServerUrl is set, where any of them can be searched. How much is written comes from the
// Serilog section of the settings.
public static class LoggingSetup
{
    // Who a line was written for: a request's signed-in user.
    public const string UserIdProperty = "UserId";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    // The host's own logger rather than Serilog's static one: the tests run several hosts at once.
    public static IServiceCollection AddSmartDeskLogging(this IServiceCollection services) =>
        services.AddSerilog(
            (provider, logger) =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                logger
                    .ReadFrom.Configuration(configuration)
                    .ReadFrom.Services(provider)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture);
                if (configuration["Seq:ServerUrl"] is { Length: > 0 } seq)
                {
                    logger.WriteTo.Seq(
                        seq,
                        apiKey: configuration["Seq:ApiKey"] is { Length: > 0 } key ? key : null,
                        formatProvider: CultureInfo.InvariantCulture);
                }
            },
            preserveStaticLogger: true);

    // A line for every request: its method and path - never the query string, which can carry a
    // token (SignalR's, a calendar feed's) - its status, how long it took, and whose it was.
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // The host's logger: by default these lines go to the static one, left silent above.
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            options.GetLevel = LevelOf;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                if (UserIdOf(context) is { } userId)
                {
                    diagnostics.Set(UserIdProperty, userId);
                }
            };
        });

    // Every line written while a request runs says whose it was. After authentication, which
    // says who that is.
    public static IApplicationBuilder UseUserInLogs(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            using (UserIdOf(context) is { } userId ? LogContext.PushProperty(UserIdProperty, userId) : null)
            {
                await next(context);
            }
        });

    // A failure is an error. Health checks are below what's written: a container asks every ten
    // seconds, and a line each time would bury the rest.
    public static LogEventLevel LevelOf(HttpContext context, double elapsedMilliseconds, Exception? exception) =>
        exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : context.Request.Path.StartsWithSegments("/health")
                ? LogEventLevel.Verbose
                : LogEventLevel.Information;

    private static Guid? UserIdOf(HttpContext context) =>
        context.RequestServices.GetService<ICurrentUserService>()?.UserId;
}
