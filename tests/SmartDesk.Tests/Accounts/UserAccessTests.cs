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
using SmartDesk.Application.Common.Security;
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

    private const string Stamp = "stamp-1";

    private static UserAccessSnapshot Access(
        string role, AccountStatus status = AccountStatus.Active, string stamp = Stamp, Guid? managerId = null, bool mustChange = false) =>
        new(status, new HashSet<string> { role }, stamp, managerId, mustChange);

    private static Claim[] Token(Guid userId, string role, string? stamp = Stamp) =>
        stamp is null
            ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)]
            : [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role), new Claim(SmartDeskClaims.SessionStamp, stamp)];

    public static TheoryData<string, UserAccessSnapshot?> StaleAccounts => new()
    {
        { "gone", null },
        { "disabled", Access(Roles.Manager, AccountStatus.Disabled) },
        { "waiting", Access(Roles.Manager, AccountStatus.PendingApproval) },
        { "role changed", Access(Roles.Employee) },
        { "password changed since", Access(Roles.Manager, stamp: "stamp-2") },
    };

    [Theory]
    [MemberData(nameof(StaleAccounts))]
    public async Task ABearerTokenForAnAccountThatChanged_FailsAuthentication(string why, UserAccessSnapshot? access)
    {
        var userId = Guid.NewGuid();

        var context = TokenValidated(Checker(userId, access), Token(userId, Roles.Manager));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result.Should().NotBeNull(why);
        context.Result!.Failure!.Message.Should().Be(JwtBearerSetup.StaleAccessMessage);
    }

    [Fact]
    public async Task ATokenIssuedWithoutASessionStamp_FailsAuthentication()
    {
        var userId = Guid.NewGuid();

        var context = TokenValidated(Checker(userId, Access(Roles.Employee)), Token(userId, Roles.Employee, stamp: null));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result!.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task ACurrentToken_GetsTheTeamTheAccountIsInNow()
    {
        var managerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var manager = TokenValidated(Checker(managerId, Access(Roles.Manager)), Token(managerId, Roles.Manager));
        var employee = TokenValidated(Checker(employeeId, Access(Roles.Employee, managerId: managerId)), Token(employeeId, Roles.Employee));
        var admin = TokenValidated(Checker(adminId, Access(Roles.Admin)), Token(adminId, Roles.Admin));
        foreach (var context in new[] { manager, employee, admin })
        {
            await JwtBearerSetup.RequireCurrentAccessAsync(context);
            context.Result.Should().BeNull();
        }

        manager.Principal!.FindFirstValue(SmartDeskClaims.Team).Should().Be(managerId.ToString());
        employee.Principal!.FindFirstValue(SmartDeskClaims.Team).Should().Be(managerId.ToString());
        admin.Principal!.FindFirstValue(SmartDeskClaims.Team).Should().BeNull();
        employee.Principal!.HasClaim(SmartDeskClaims.PasswordChangeRequired, "true").Should().BeFalse();
    }

    [Fact]
    public async Task ATeamClaimInTheTokenItself_IsIgnored()
    {
        var userId = Guid.NewGuid();
        var claims = Token(userId, Roles.Employee).Append(new Claim(SmartDeskClaims.Team, Guid.NewGuid().ToString())).ToArray();

        var context = TokenValidated(Checker(userId, Access(Roles.Employee)), claims);
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        // Only what the account says counts: an employee outside any team has none.
        context.Result.Should().BeNull();
        context.Principal!.FindFirstValue(SmartDeskClaims.Team).Should().BeNull();
    }

    [Fact]
    public async Task AnAccountThatMustChangeItsPassword_IsMarked()
    {
        var userId = Guid.NewGuid();

        var context = TokenValidated(Checker(userId, Access(Roles.Employee, mustChange: true)), Token(userId, Roles.Employee));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result.Should().BeNull();
        context.Principal!.HasClaim(SmartDeskClaims.PasswordChangeRequired, "true").Should().BeTrue();
    }

    [Fact]
    public async Task ABearerTokenWithoutAUser_FailsAuthentication()
    {
        var checker = new Mock<IUserAccessChecker>(MockBehavior.Strict);

        var context = TokenValidated(checker.Object, new Claim(ClaimTypes.Role, Roles.Admin));
        await JwtBearerSetup.RequireCurrentAccessAsync(context);

        context.Result!.Failure.Should().NotBeNull();
    }

    private static IUserAccessChecker Checker(Guid userId, UserAccessSnapshot? access)
    {
        var checker = new Mock<IUserAccessChecker>(MockBehavior.Strict);
        checker.Setup(c => c.GetAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(access);
        return checker.Object;
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
