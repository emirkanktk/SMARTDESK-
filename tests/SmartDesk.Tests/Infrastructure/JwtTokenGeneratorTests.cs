using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;

using SmartDesk.Application.Common.Security;

namespace SmartDesk.Tests.Infrastructure;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "SmartDesk",
        Audience = "SmartDesk",
        Key = "test-signing-key-that-is-at-least-32-bytes-long",
        AccessTokenLifetimeMinutes = 30,
    };

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserProfile User =
        new(Guid.NewGuid(), "System", "Admin", "admin@smartdesk.local", [Roles.Admin, Roles.Employee]);

    private static JwtTokenGenerator CreateGenerator() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(Now));

    [Fact]
    public void Generate_ExpiresAfterConfiguredLifetime()
    {
        var token = CreateGenerator().Generate(User, Guid.NewGuid());

        token.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddMinutes(30));
        token.ExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Generate_CarriesUserIdentityAndEveryRole()
    {
        var jwt = new JsonWebToken(CreateGenerator().Generate(User, Guid.NewGuid()).Token);

        jwt.Subject.Should().Be(User.Id.ToString());
        jwt.GetClaim(JwtRegisteredClaimNames.Email).Value.Should().Be(User.Email);
        jwt.Claims.Where(c => c.Type == JwtTokenGenerator.RoleClaimType).Select(c => c.Value)
            .Should().BeEquivalentTo(Roles.Admin, Roles.Employee);
        jwt.Issuer.Should().Be("SmartDesk");
        jwt.Audiences.Should().ContainSingle("SmartDesk");
    }

    [Fact]
    public void Generate_TiesTheTokenToTheSessionStamp_AndLeavesTeamAndPasswordStateToTheServer()
    {
        var user = User with { SessionStamp = "abc123", ManagerId = Guid.NewGuid(), MustChangePassword = true };

        var jwt = new JsonWebToken(CreateGenerator().Generate(user, Guid.NewGuid()).Token);

        jwt.GetClaim(SmartDeskClaims.SessionStamp).Value.Should().Be("abc123");
        jwt.Claims.Should().NotContain(c => c.Type == SmartDeskClaims.Team || c.Type == SmartDeskClaims.PasswordChangeRequired);
    }

    [Fact]
    public async Task Generate_TokenValidatesAgainstTheSameKeyOnly()
    {
        var token = CreateGenerator().Generate(User, Guid.NewGuid()).Token;
        var handler = new JsonWebTokenHandler();

        var withRightKey = await handler.ValidateTokenAsync(token, ValidationParameters(Options.Key));
        var withWrongKey = await handler.ValidateTokenAsync(token, ValidationParameters("a-different-key-that-is-also-32-bytes-long"));

        withRightKey.IsValid.Should().BeTrue();
        withWrongKey.IsValid.Should().BeFalse();
    }

    // Lifetime isn't validated here: the token is issued at a fixed past instant by design.
    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudience = Options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = false,
    };
}
