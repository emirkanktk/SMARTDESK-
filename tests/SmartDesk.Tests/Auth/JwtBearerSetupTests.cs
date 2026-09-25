using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartDesk.API.Authentication;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.Tests.Auth;

// The API's token rules against real tokens. Lifetime is checked against the real clock, so
// tokens are issued relative to it.
public class JwtBearerSetupTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "SmartDesk",
        Audience = "SmartDesk",
        // Long enough for HS512 too, so the wrong-algorithm test can sign with the same key.
        Key = "test-signing-key-" + new string('x', 64),
        AccessTokenLifetimeMinutes = 60,
    };

    private static readonly UserProfile User =
        new(Guid.NewGuid(), "Ayşe", "Yılmaz", "employee1@smartdesk.local", [Roles.Employee]);

    private static string IssuedAt(DateTimeOffset issuedAt) =>
        new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(issuedAt))
            .Generate(User).Token;

    private static Task<TokenValidationResult> ValidateAsync(string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, JwtBearerSetup.ValidationParameters(Options));

    [Fact]
    public async Task AFreshToken_IsAccepted()
    {
        var result = await ValidateAsync(IssuedAt(DateTimeOffset.UtcNow));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ATokenThatExpiredSecondsAgo_IsStillAccepted_ForClockDrift()
    {
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-Options.AccessTokenLifetimeMinutes).AddSeconds(-10);

        (await ValidateAsync(IssuedAt(issuedAt))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ATokenThatExpiredAMinuteAgo_IsRefused()
    {
        // With the library's default five-minute skew this one would still get in.
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-Options.AccessTokenLifetimeMinutes - 1);

        var result = await ValidateAsync(IssuedAt(issuedAt));

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenExpiredException>();
    }

    [Fact]
    public async Task ATokenSignedWithAnotherAlgorithm_IsRefused_EvenWithTheRightKey()
    {
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Options.Issuer,
            Audience = Options.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, User.Id.ToString())]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.Key)), SecurityAlgorithms.HmacSha512),
        });

        // Without ValidAlgorithms this token is accepted: the key is right, only the algorithm
        // differs. (IdentityModel reports the refusal as "no matching key", not by algorithm.)
        (await ValidateAsync(token)).IsValid.Should().BeFalse();
    }
}
