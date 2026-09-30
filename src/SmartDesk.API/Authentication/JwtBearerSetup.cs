using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Security;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.API.Authentication;

public static class JwtBearerSetup
{
    // How far a token's expiry may be off because of clock drift between machines. The
    // library default is five minutes, which quietly lets a 15-minute token work for 20.
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

    public const string StaleAccessMessage =
        "This session has ended: the user signed out, the account was switched off, or its role or password changed. Sign in again.";

    // Runs once a token has passed the checks above (ADR-0020, ADR-0021). A token stays valid
    // until it expires, so on its own it would keep a disabled user in, a demoted Admin an
    // Admin, or a session going after its password was changed, for up to an hour. Its user must
    // still be Active, hold exactly the roles it lists, have the security stamp it was issued
    // under, and the sign-in session it names must still be open (ADR-0026); otherwise the request
    // is unauthenticated (401) and the client renews its session or signs in again.
    // What only the account knows now - its team, a pending password change - is added to the
    // request's user here, never taken from the token.
    public static async Task RequireCurrentAccessAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || principal!.Identity is not ClaimsIdentity identity)
        {
            context.Fail("The token doesn't name a user.");
            return;
        }

        var checker = context.HttpContext.RequestServices.GetRequiredService<IUserAccessChecker>();
        var access = await checker.GetAsync(userId, context.HttpContext.RequestAborted);
        var tokenRoles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value);
        if (access is not { Status: AccountStatus.Active }
            || !access.Roles.SetEquals(tokenRoles)
            || principal.FindFirstValue(SmartDeskClaims.SessionStamp) != access.SessionStamp
            || !Guid.TryParse(principal.FindFirstValue(SmartDeskClaims.Session), out var sessionId)
            || !access.OpenSessions.Contains(sessionId))
        {
            context.Fail(StaleAccessMessage);
            return;
        }

        // Never from the token: only the values set here count.
        foreach (var claim in identity.FindAll(c => c.Type is SmartDeskClaims.Team or SmartDeskClaims.PasswordChangeRequired).ToList())
        {
            identity.RemoveClaim(claim);
        }

        if (Teams.TeamOf(userId, access.Roles.ToList(), access.ManagerId) is { } team)
        {
            identity.AddClaim(new Claim(SmartDeskClaims.Team, team.ToString()));
        }

        if (access.MustChangePassword)
        {
            identity.AddClaim(new Claim(SmartDeskClaims.PasswordChangeRequired, "true"));
        }
    }
}
