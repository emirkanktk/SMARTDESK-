using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Common.Security;
using SmartDesk.Application.Resources;

namespace SmartDesk.API.Authentication;

// Marks the endpoints a user who must change their password can still reach: who am I, and
// change my password (ADR-0021).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute;

// While an account's password is one someone else chose, its sessions can do nothing but
// replace it. Enforced here, on the server, for every endpoint - hiding screens in the
// frontend would leave the API itself open. Runs after authentication and authorization, so
// an anonymous or forbidden request still gets its 401 or 403 first.
public static class PasswordChangeGate
{
    public const string Code = "password-change-required";
    public static string Detail => Messages.Error_PasswordChangeRequired;

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.HasClaim(SmartDeskClaims.PasswordChangeRequired, "true")
                && context.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredAttribute>() is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = Messages.Problem_PasswordChangeRequired,
                    Detail = Detail,
                    Extensions = { ["code"] = Code },
                };
                await context.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
                return;
            }

            await next(context);
        });
}
