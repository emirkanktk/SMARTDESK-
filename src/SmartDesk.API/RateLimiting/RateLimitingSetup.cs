using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.Application.Resources;

namespace SmartDesk.API.RateLimiting;

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";

    // Every question to the assistant is a paid call to its language model (ADR-0029).
    public const string AssistantPolicy = "assistant";

    // Checking in with a code (ADR-0034): a person scans a few times at most, a script guessing
    // codes far more.
    public const string CheckInPolicy = "check-in";

    // A door screen asks without a user, so it's limited by its address. One screen asks about
    // six times a minute (just after each 30-second code change, and every 10 seconds), and a
    // building's screens usually reach the API from one address: the default leaves room for a
    // hundred of them.
    public const string DoorScreenPolicy = "door-screen";

    // Reporting a problem (ADR-0037): someone at a door reports one or two, and each may carry
    // a photo of up to 2 MB, so a script sending them by the hundred is what this stops.
    public const string IssueReportPolicy = "issue-report";

    // A personal calendar feed (ADR-0048) is fetched without a user, so it's limited by address.
    // A calendar app fetches each feed every hour or so, but Google's and Microsoft's servers
    // fetch everyone's from a few addresses: the default leaves room for that.
    public const string CalendarFeedPolicy = "calendar-feed";

    // Identity's lockout (5 failures -> 5 minutes) protects one account at a time; this caps
    // how many sign-in attempts one client can make across all accounts, which is what a
    // password-spraying or credential-stuffing run looks like.
    //
    // Partitioned by client IP. Behind a reverse proxy, that's the client's own address only when
    // the proxy is trusted for forwarded headers (ADR-0028); otherwise everyone shares one limit.
    public static IServiceCollection AddSmartDeskRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitsPerMinute = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
        var assistantPermitsPerMinute = configuration.GetValue("RateLimiting:AssistantPermitsPerMinute", 20);
        var checkInPermitsPerMinute = configuration.GetValue("RateLimiting:CheckInPermitsPerMinute", 20);
        var doorScreenPermitsPerMinute = configuration.GetValue("RateLimiting:DoorScreenPermitsPerMinute", 600);
        var issueReportPermitsPerMinute = configuration.GetValue("RateLimiting:IssueReportPermitsPerMinute", 10);
        var calendarFeedPermitsPerMinute = configuration.GetValue("RateLimiting:CalendarFeedPermitsPerMinute", 120);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(LoginPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Per signed-in user, whatever their address: the endpoint needs a token anyway.
            options.AddPolicy(AssistantPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = assistantPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(CheckInPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = checkInPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(DoorScreenPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = doorScreenPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(IssueReportPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = issueReportPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(CalendarFeedPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = calendarFeedPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Same ProblemDetails shape as every other error, plus Retry-After.
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = Messages.Problem_TooManyRequests,
                            Detail = policy switch
                            {
                                AssistantPolicy => Messages.Error_TooManyAssistantRequests,
                                CheckInPolicy => Messages.Error_TooManyCheckIns,
                                DoorScreenPolicy => Messages.Error_TooManyDoorScreenRequests,
                                IssueReportPolicy => Messages.Error_TooManyIssueReports,
                                CalendarFeedPolicy => Messages.Error_TooManyCalendarFeedRequests,
                                _ => Messages.Error_TooManySignIns,
                            },
                        },
                    });
            };
        });

        return services;
    }
}
