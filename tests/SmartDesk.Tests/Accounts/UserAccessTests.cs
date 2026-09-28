using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SmartDesk.API.Authentication;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Accounts;

// Whether a still-valid token's user is still allowed in as the token describes them.
public sealed class UserAccessTests : IDisposable
{
    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task AnActiveUser_WithTheTokensRoles_IsCurrent()
    {
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);

        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();
        (await _host.IsCurrentAsync(member, Roles.Admin)).Should().BeFalse("the token claims a role they don't have");
        (await _host.IsCurrentAsync(member, Roles.Employee, Roles.Admin)).Should().BeFalse();
        (await _host.IsCurrentAsync(member)).Should().BeFalse("the token lost a role they have");
    }

    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.PendingApproval)]
    public async Task AnAccountThatIsntActive_IsNotCurrent(AccountStatus status)
    {
        var user = await _host.AddUserAsync("user@company.example", Roles.Manager, status);

        (await _host.IsCurrentAsync(user, Roles.Manager)).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownUser_IsNotCurrent()
    {
        (await _host.IsCurrentAsync(Guid.NewGuid(), Roles.Employee)).Should().BeFalse();
    }

    [Fact]
    public async Task TheAnswerIsRemembered_UntilUserAccountsChangesTheAccount()
    {
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();

        // Changed behind its back: still the remembered answer, for up to CacheFor.
        await _host.WithAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(member.ToString()))!;
            user.Status = AccountStatus.Disabled;
            return (await users.UpdateAsync(user)).Succeeded;
        });
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();

        // Changed through UserAccounts: the next check reads the database again.
        await _host.WithAsync(async sp =>
        {
            await sp.GetRequiredService<IUserAccounts>().SetStatusAsync(member, AccountStatus.Disabled, CancellationToken.None);
            return true;
        });
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeFalse();
        UserAccessChecker.CacheFor.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ABearerTokenForAnAccountThatIsntCurrent_FailsAuthentication()
    {
        var userId = Guid.NewGuid();
        var checker = new Mock<IUserAccessChecker>();
        checker.Setup(c => c.IsCurrentAsync(userId, It.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(new[] { Roles.Manager })), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var context = TokenValidated(checker.Object, new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, Roles.Manager));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result.Should().NotBeNull();
        context.Result!.Failure!.Message.Should().Be(JwtBearerSetup.StaleAccessMessage);
    }

    [Fact]
    public async Task ABearerTokenForACurrentAccount_IsLeftAlone()
    {
        var userId = Guid.NewGuid();
        var checker = new Mock<IUserAccessChecker>();
        checker.Setup(c => c.IsCurrentAsync(userId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = TokenValidated(checker.Object, new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, Roles.Employee));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ABearerTokenWithoutAUser_FailsAuthentication()
    {
        var checker = new Mock<IUserAccessChecker>(MockBehavior.Strict);

        var context = TokenValidated(checker.Object, new Claim(ClaimTypes.Role, Roles.Admin));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result!.Failure.Should().NotBeNull();
    }

    private static TokenValidatedContext TokenValidated(IUserAccessChecker checker, params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(checker).BuildServiceProvider(),
        };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };
    }
}
