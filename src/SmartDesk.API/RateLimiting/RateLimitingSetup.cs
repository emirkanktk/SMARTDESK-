using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.Application.Resources;

namespace SmartDesk.API.RateLimiting;

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";

    // Identity's lockout (5 failures -> 5 minutes) protects one account at a time; this caps
    // how many sign-in attempts one client can make across all accounts, which is what a
    // password-spraying or credential-stuffing run looks like.
    //
    // Partitioned by client IP. Behind a reverse proxy, that's the client's own address only when
    // the proxy is trusted for forwarded headers (ADR-0028); otherwise everyone shares one limit.
    public static IServiceCollection AddSmartDeskRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitsPerMinute = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);

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

            // Same ProblemDetails shape as every other error, plus Retry-After.
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = Messages.Problem_TooManyRequests,
                            Detail = Messages.Error_TooManySignIns,
                        },
                    });
            };
        });

        return services;
    }
}
