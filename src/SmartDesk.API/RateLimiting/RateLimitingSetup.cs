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

    // A door screen asks every few seconds without a user, so it's limited by its address.
    public const string DoorScreenPolicy = "door-screen";

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
        var doorScreenPermitsPerMinute = configuration.GetValue("RateLimiting:DoorScreenPermitsPerMinute", 60);

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
                                _ => Messages.Error_TooManySignIns,
                            },
                        },
                    });
            };
        });

        return services;
    }
}
