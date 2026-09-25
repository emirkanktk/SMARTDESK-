using System.Text;
using Microsoft.IdentityModel.Tokens;
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
}
