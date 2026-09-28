using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Accounts.Commands.AssignManager;
using SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;
using SmartDesk.Application.Features.Accounts.Commands.CreateAccount;
using SmartDesk.Application.Features.Accounts.Commands.SendPasswordReset;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Accounts.Queries.GetAccounts;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.ResetPassword;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Accounts;

// What an Admin can do to other accounts, and the rules that keep the system manageable.
public sealed class AccountManagementTests : IDisposable
{
    private readonly AccountsTestHost _host = new();
    private Guid _admin;

    public void Dispose() => _host.Dispose();

    private async Task<Guid> AdminAsync() =>
        _admin = await _host.AddUserAsync("admin@company.example", Roles.Admin, firstName: "Ada", lastName: "Admin");

    private Task<T> AsAdmin<T>(MediatR.IRequest<T> request) => _host.SendAsync(_admin, Roles.Admin, request);

    [Fact]
    public async Task TheList_PutsWaitingSignUpsFirst_ThenSortsByName_AndFilters()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        await _host.AddUserAsync("mehmet@company.example", Roles.Employee, managerId: manager, firstName: "Mehmet", lastName: "Demir");
        await _host.AddUserAsync("burak@company.example", Roles.Employee, AccountStatus.Disabled, firstName: "Burak", lastName: "Can");
        await _host.AddUserAsync("waiting@company.example", Roles.Manager, AccountStatus.PendingApproval, firstName: "Yusuf", lastName: "Er");

        var all = await AsAdmin(new GetAccountsQuery());

        all.Select(a => a.Email).Should().Equal(
            "waiting@company.example", "admin@company.example", "burak@company.example", "mehmet@company.example", "zeynep@company.example");
        all.Single(a => a.Email == "mehmet@company.example").ManagerName.Should().Be("Zeynep Kaya");
        all.Single(a => a.Email == "zeynep@company.example").TeamSize.Should().Be(1);

        (await AsAdmin(new GetAccountsQuery(Role: Roles.Employee))).Select(a => a.FirstName).Should().Equal("Burak", "Mehmet");
        (await AsAdmin(new GetAccountsQuery(Status: AccountStatus.Disabled))).Should().ContainSingle(a => a.FirstName == "Burak");
        (await AsAdmin(new GetAccountsQuery(Search: "zeynep kaya"))).Should().ContainSingle(a => a.FirstName == "Zeynep");
        (await AsAdmin(new GetAccountsQuery(Search: "COMPANY.EXAMPLE"))).Should().HaveCount(5);

        var badRole = () => AsAdmin(new GetAccountsQuery(Role: "Owner"));
        await badRole.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task AnAccountWithTwoRoles_IsListedUnderTheHigherOneOnly()
    {
        // BootstrapAdmin adds Admin to an existing account without taking its old role away.
        await AdminAsync();
        var both = await _host.AddUserAsync("it@company.example", Roles.Employee);
        await _host.WithAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.AddToRoleAsync((await users.FindByIdAsync(both.ToString()))!, Roles.Admin);
        });

        (await _host.FindAsync(both))!.Role.Should().Be(Roles.Admin);
        (await AsAdmin(new GetAccountsQuery(Role: Roles.Admin))).Select(a => a.Id).Should().Contain(both);
        (await AsAdmin(new GetAccountsQuery(Role: Roles.Employee))).Select(a => a.Id).Should().NotContain(both);
    }

    [Fact]
    public async Task ChangingARole_TakesEffectOnTheirNextRequest()
    {
        await AdminAsync();
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();

        var changed = await AsAdmin(new ChangeAccountRoleCommand(Roles.Admin) { Id = member });

        changed.Role.Should().Be(Roles.Admin);
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeFalse("the cached answer was dropped");
        (await _host.IsCurrentAsync(member, Roles.Admin)).Should().BeTrue();
    }

    [Fact]
    public async Task PromotingATeamMember_TakesThemOutOfTheTeam()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager);
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee, managerId: manager);

        var promoted = await AsAdmin(new ChangeAccountRoleCommand(Roles.Manager) { Id = member });

        promoted.Should().BeEquivalentTo(new { Role = Roles.Manager, ManagerId = (Guid?)null, ManagerName = (string?)null });
        (await _host.FindAsync(manager))!.TeamSize.Should().Be(0);
    }

    [Fact]
    public async Task AManagerWithATeam_CantBeGivenAnotherRole()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        await _host.AddUserAsync("a@company.example", Roles.Employee, managerId: manager);
        await _host.AddUserAsync("b@company.example", Roles.Employee, managerId: manager);

        var act = () => AsAdmin(new ChangeAccountRoleCommand(Roles.Employee) { Id = manager });

        await act.Should().ThrowAsync<ConflictException>().WithMessage("Zeynep Kaya still has 2 team member(s).*");
        (await _host.FindAsync(manager))!.Role.Should().Be(Roles.Manager);
    }

    [Fact]
    public async Task NobodyChangesTheirOwnRole_OrAWaitingSignUps()
    {
        await AdminAsync();
        var pending = await _host.AddUserAsync("waiting@company.example", Roles.Manager, AccountStatus.PendingApproval);

        var self = () => AsAdmin(new ChangeAccountRoleCommand(Roles.Employee) { Id = _admin });
        var waiting = () => AsAdmin(new ChangeAccountRoleCommand(Roles.Employee) { Id = pending });
        var unknownRole = () => AsAdmin(new ChangeAccountRoleCommand("Owner") { Id = pending });

        await self.Should().ThrowAsync<ConflictException>().WithMessage("You can't change your own role*");
        await waiting.Should().ThrowAsync<ConflictException>().WithMessage("*Approve or reject it first.");
        (await unknownRole.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Role");
        (await _host.FindAsync(_admin))!.Role.Should().Be(Roles.Admin);
    }

    [Fact]
    public async Task TheLastActiveAdmin_CantBeDemotedOrSwitchedOff()
    {
        // Reachable when the caller's own admin rights went away mid-request; the guard doesn't
        // rely on the caller being an Admin.
        var lastAdmin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        await _host.AddUserAsync("off-admin@company.example", Roles.Admin, AccountStatus.Disabled);
        var caller = Guid.NewGuid();
        var stampBefore = await ConcurrencyStampAsync(lastAdmin);

        var demote = () => _host.SendAsync(caller, Roles.Admin, new ChangeAccountRoleCommand(Roles.Employee) { Id = lastAdmin });
        var disable = () => _host.SendAsync(caller, Roles.Admin, new SetAccountStatusCommand(lastAdmin, Enabled: false));

        await demote.Should().ThrowAsync<ConflictException>().WithMessage(LastAdminGuard.Message);
        await disable.Should().ThrowAsync<ConflictException>().WithMessage(LastAdminGuard.Message);
        (await _host.FindAsync(lastAdmin)).Should().BeEquivalentTo(new { Role = Roles.Admin, Status = AccountStatus.Active });

        // Refused before anything was written, not changed and undone: meanwhile the admin's
        // own requests would have been turned away.
        (await ConcurrencyStampAsync(lastAdmin)).Should().Be(stampBefore);
    }

    private Task<string?> ConcurrencyStampAsync(Guid id) =>
        _host.WithAsync(async sp =>
            (await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString()))!.ConcurrencyStamp);

    [Fact]
    public async Task TwoAdminsDemotingEachOtherAtOnce_LeaveOneAdmin()
    {
        var first = await _host.AddUserAsync("first@company.example", Roles.Admin);
        var second = await _host.AddUserAsync("second@company.example", Roles.Admin);
        var secondAccount = (await _host.FindAsync(second))!;

        // The other request's demotion lands between this one's count and its change.
        var act = () => _host.WithAsync<bool>(async sp =>
        {
            var accounts = sp.GetRequiredService<IUserAccounts>();
            await LastAdminGuard.ChangeAsync(
                accounts,
                secondAccount,
                async () =>
                {
                    await accounts.SetRoleAsync(first, Roles.Employee, CancellationToken.None);
                    await accounts.SetRoleAsync(second, Roles.Employee, CancellationToken.None);
                },
                () => accounts.SetRoleAsync(second, Roles.Admin, CancellationToken.None),
                CancellationToken.None);
            return true;
        });

        await act.Should().ThrowAsync<ConflictException>().WithMessage(LastAdminGuard.Message);
        (await _host.FindAsync(second))!.Role.Should().Be(Roles.Admin);
    }

    [Fact]
    public async Task Disabling_LocksThemOut_AndEnablingLetsThemBackIn()
    {
        await AdminAsync();
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);
        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();

        (await AsAdmin(new SetAccountStatusCommand(member, Enabled: false))).Status.Should().Be(AccountStatus.Disabled);

        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeFalse("their existing token stops working");
        var login = () => _host.SendAnonymousAsync(new LoginCommand("member@company.example", AccountsTestHost.Password));
        await login.Should().ThrowAsync<ForbiddenAccessException>();

        (await AsAdmin(new SetAccountStatusCommand(member, Enabled: true))).Status.Should().Be(AccountStatus.Active);

        (await _host.IsCurrentAsync(member, Roles.Employee)).Should().BeTrue();
        (await login()).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NobodySwitchesOffThemselves_OrAWaitingSignUp()
    {
        await AdminAsync();
        var pending = await _host.AddUserAsync("waiting@company.example", Roles.Manager, AccountStatus.PendingApproval);

        var self = () => AsAdmin(new SetAccountStatusCommand(_admin, Enabled: false));
        var waiting = () => AsAdmin(new SetAccountStatusCommand(pending, Enabled: true));

        await self.Should().ThrowAsync<ConflictException>();
        await waiting.Should().ThrowAsync<ConflictException>().WithMessage("*Approve or reject it instead.");
        (await _host.FindAsync(pending))!.Status.Should().Be(AccountStatus.PendingApproval);
    }

    [Fact]
    public async Task AnEmployee_CanBeMovedBetweenTeams_OnlyToAnActiveManager()
    {
        await AdminAsync();
        var zeynep = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        var waitingManager = await _host.AddUserAsync("waiting@company.example", Roles.Manager, AccountStatus.PendingApproval);
        var otherEmployee = await _host.AddUserAsync("other@company.example", Roles.Employee);
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);

        (await AsAdmin(new AssignManagerCommand(zeynep) { Id = member })).ManagerName.Should().Be("Zeynep Kaya");

        foreach (var notAManager in new[] { waitingManager, otherEmployee, Guid.NewGuid() })
        {
            var act = () => AsAdmin(new AssignManagerCommand(notAManager) { Id = member });
            (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "ManagerId");
        }

        (await AsAdmin(new AssignManagerCommand(null) { Id = member })).ManagerId.Should().BeNull();

        var managerIntoTeam = () => AsAdmin(new AssignManagerCommand(zeynep) { Id = waitingManager });
        await managerIntoTeam.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ANewAccount_HasNoPasswordUntilItsOwnerChoosesOne_FromTheInvitation()
    {
        await AdminAsync();

        var created = await AsAdmin(new CreateAccountCommand("Elif", "Şahin", " elif@company.example ", Roles.Employee));

        created.Should().BeEquivalentTo(new { Email = "elif@company.example", Role = Roles.Employee, Status = AccountStatus.Active });
        var noPasswordYet = () => _host.SendAnonymousAsync(new LoginCommand("elif@company.example", AccountsTestHost.Password));
        await noPasswordYet.Should().ThrowAsync<UnauthorizedException>();

        var invitation = _host.Emails.Sent.Should().ContainSingle().Subject;
        invitation.Subject.Should().Be("You've been added to SmartDesk");
        invitation.Text.Should().StartWith("Hello Elif,").And.Contain("Ada Admin created a SmartDesk account for you");
        CapturingEmailSender.LinkIn(invitation, "http://localhost:4200/forgot-password").Should().NotBeNull();
        var link = new Uri(CapturingEmailSender.LinkIn(invitation, "http://localhost:4200/reset-password?"));
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);

        await _host.SendAnonymousAsync(new ResetPasswordCommand(query["email"]!, query["token"]!, "Elifs-Own-Pass-1"));

        (await _host.SendAnonymousAsync(new LoginCommand("elif@company.example", "Elifs-Own-Pass-1"))).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ANewAccount_NeedsAFreeEmail_AndATeamOnlyForAnEmployee()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager);

        var taken = () => AsAdmin(new CreateAccountCommand("Ada", "Again", "ADMIN@company.example", Roles.Employee));
        var adminInTeam = () => AsAdmin(new CreateAccountCommand("New", "Admin", "new@company.example", Roles.Admin, manager));

        await taken.Should().ThrowAsync<ConflictException>().WithMessage(AccountRules.EmailTaken);
        (await adminInTeam.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "ManagerId");
        _host.Emails.Sent.Should().BeEmpty();

        var member = await AsAdmin(new CreateAccountCommand("New", "Member", "new@company.example", Roles.Employee, manager));
        member.ManagerId.Should().Be(manager);
    }

    [Fact]
    public async Task APasswordResetLink_GoesOnlyToActiveAccounts()
    {
        await AdminAsync();
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee, firstName: "Mehmet");
        var disabled = await _host.AddUserAsync("off@company.example", Roles.Employee, AccountStatus.Disabled);

        await _host.SendAsync(_admin, Roles.Admin, new SendPasswordResetCommand(member));
        var act = () => _host.SendAsync(_admin, Roles.Admin, new SendPasswordResetCommand(disabled));

        await act.Should().ThrowAsync<ConflictException>();
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        (email.To, email.Subject).Should().Be(("member@company.example", "Reset your SmartDesk password"));
    }
}
