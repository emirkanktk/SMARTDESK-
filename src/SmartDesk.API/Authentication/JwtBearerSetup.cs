using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.API.Authentication;

public static class JwtBearerSetup
{
    // How far a token's expiry may be off because of clock drift between machines. The
    // library default is five minutes, which quietly lets a 60-minute token work for 65.
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    // What an incoming bearer token must satisfy. Kept out of Program.cs so the rules can be
    // tested against real tokens from JwtTokenGenerator.
    public static TokenValidationParameters ValidationParameters(JwtOptions jwt) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),

        // Only the algorithm JwtTokenGenerator signs with. Anything else in a token's header
        // is refused rather than tried with our key.
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = ClockSkew,
    };

    public const string StaleAccessMessage = "This account has been switched off or its role has changed. Sign in again.";

    // Runs once a token has passed the checks above (ADR-0020). A token stays valid until it
    // expires, so on its own it would keep a disabled user in, or a demoted Admin an Admin, for
    // up to an hour. Its user must still be Active and hold exactly the roles it lists;
    // otherwise the request is unauthenticated (401) and the client signs in again.
    public static async Task RequireCurrentAccessAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("The token doesn't name a user.");
            return;
        }

        var roles = principal!.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
        var checker = context.HttpContext.RequestServices.GetRequiredService<IUserAccessChecker>();
        if (!await checker.IsCurrentAsync(userId, roles, context.HttpContext.RequestAborted))
        {
            context.Fail(StaleAccessMessage);
        }
    }
}
