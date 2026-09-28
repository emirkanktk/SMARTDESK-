using FluentAssertions;
using FluentValidation;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Security;
using SmartDesk.Application.Features.Accounts.Commands.AssignManager;
using SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;
using SmartDesk.Application.Features.Accounts.Commands.CreateAccount;
using SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;
using SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Accounts.Queries.GetAccounts;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.ChangePassword;
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
    public async Task AManagerWhoseTeamHasResources_CantBeGivenAnotherRole()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        var room = await _host.Database.AddResourceAsync("Meeting Room B");
        await using (var context = _host.Database.CreateContext())
        {
            context.ResourceTeams.Add(new SmartDesk.Domain.Entities.ResourceTeam { ResourceId = room.Id, TeamId = manager });
            await context.SaveChangesAsync();
        }

        var act = () => AsAdmin(new ChangeAccountRoleCommand(Roles.Employee) { Id = manager });

        await act.Should().ThrowAsync<ConflictException>().WithMessage("Zeynep Kaya's team still has 1 resource(s).*");
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
        _host.RealtimeSessions.Ended.Should().Contain(member, "their team decides what they can use");

        foreach (var notAManager in new[] { waitingManager, otherEmployee, Guid.NewGuid() })
        {
            var act = () => AsAdmin(new AssignManagerCommand(notAManager) { Id = member });
            (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "ManagerId");
        }

        (await AsAdmin(new AssignManagerCommand(null) { Id = member })).ManagerId.Should().BeNull();

        var managerIntoTeam = () => AsAdmin(new AssignManagerCommand(zeynep) { Id = waitingManager });
        await managerIntoTeam.Should().ThrowAsync<ConflictException>();
    }

    private const string Temporary = "Temp-Pass-2026";

    [Fact]
    public async Task ANewAccount_SignsInWithTheTemporaryPassword_AndMustReplaceIt()
    {
        await AdminAsync();

        var created = await AsAdmin(new CreateAccountCommand("Elif", "Şahin", " elif@company.example ", Roles.Employee, Temporary));

        created.Should().BeEquivalentTo(new { Email = "elif@company.example", Role = Roles.Employee, Status = AccountStatus.Active, MustChangePassword = true });
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("You've been added to SmartDesk");
        email.Text.Should().StartWith("Hello Elif,").And.Contain("Ada Admin created a SmartDesk account for you")
            .And.NotContain(Temporary, "the password never travels by email");

        var login = await _host.SendAnonymousAsync(new LoginCommand("elif@company.example", Temporary));
        login.PasswordChangeRequired.Should().BeTrue();
        var before = (await _host.GetAccessAsync(created.Id))!; // now remembered for 30 seconds
        before.MustChangePassword.Should().BeTrue();
        new JsonWebToken(login.AccessToken).GetClaim(SmartDeskClaims.SessionStamp).Value
            .Should().Be(before.SessionStamp, "the token is tied to the password it was issued under");

        var changed = await _host.SendAsync(created.Id, Roles.Employee, new ChangePasswordCommand(Temporary, "Elifs-Own-Pass-1"));

        changed.PasswordChangeRequired.Should().BeFalse();
        changed.AccessToken.Should().NotBe(login.AccessToken);
        var newStamp = new JsonWebToken(changed.AccessToken).GetClaim(SmartDeskClaims.SessionStamp).Value;
        var after = (await _host.GetAccessAsync(created.Id))!;
        after.MustChangePassword.Should().BeFalse("the remembered answer was dropped");
        after.SessionStamp.Should().NotBe(before.SessionStamp);
        newStamp.Should().Be(after.SessionStamp, "the new token carries the new stamp");
        (await _host.SendAnonymousAsync(new LoginCommand("elif@company.example", "Elifs-Own-Pass-1"))).PasswordChangeRequired.Should().BeFalse();
        var oldPassword = () => _host.SendAnonymousAsync(new LoginCommand("elif@company.example", Temporary));
        await oldPassword.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ANewAccount_NeedsAFreeEmail_AGoodPassword_AndATeamOnlyForAnEmployee()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager);

        var taken = () => AsAdmin(new CreateAccountCommand("Ada", "Again", "ADMIN@company.example", Roles.Employee, Temporary));
        var adminInTeam = () => AsAdmin(new CreateAccountCommand("New", "Admin", "new@company.example", Roles.Admin, Temporary, manager));
        var weak = () => AsAdmin(new CreateAccountCommand("New", "Member", "new@company.example", Roles.Employee, "weakpass"));
        var none = () => AsAdmin(new CreateAccountCommand("New", "Member", "new@company.example", Roles.Employee, ""));

        await taken.Should().ThrowAsync<ConflictException>().WithMessage(AccountRules.EmailTaken);
        (await adminInTeam.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "ManagerId");
        (await weak.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().OnlyContain(e => e.PropertyName == "Password");
        (await none.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Password");
        (await _host.FindByEmailAsync("new@company.example")).Should().BeNull();
        _host.Emails.Sent.Should().BeEmpty();

        var member = await AsAdmin(new CreateAccountCommand("New", "Member", "new@company.example", Roles.Employee, Temporary, manager));
        member.ManagerId.Should().Be(manager);
    }

    [Fact]
    public async Task ATemporaryPassword_EndsTheirSessions_AndMustBeReplaced()
    {
        await AdminAsync();
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee, firstName: "Mehmet");
        var stampBefore = (await _host.GetAccessAsync(member))!.SessionStamp;

        var updated = await AsAdmin(new SetTemporaryPasswordCommand(Temporary) { Id = member });

        updated.MustChangePassword.Should().BeTrue();
        var access = (await _host.GetAccessAsync(member))!;
        access.SessionStamp.Should().NotBe(stampBefore, "every earlier token must stop working");
        access.MustChangePassword.Should().BeTrue();
        _host.RealtimeSessions.Ended.Should().Contain(member);
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        (email.To, email.Subject).Should().Be(("member@company.example", "Your SmartDesk password was changed"));
        email.Text.Should().Contain("Ada Admin set a temporary password").And.NotContain(Temporary);

        var oldPassword = () => _host.SendAnonymousAsync(new LoginCommand("member@company.example", AccountsTestHost.Password));
        await oldPassword.Should().ThrowAsync<UnauthorizedException>();
        (await _host.SendAnonymousAsync(new LoginCommand("member@company.example", Temporary))).PasswordChangeRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ARefusedTemporaryPassword_ChangesNothing()
    {
        await AdminAsync();
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee);
        var stampBefore = (await _host.GetAccessAsync(member))!.SessionStamp;

        var weak = () => AsAdmin(new SetTemporaryPasswordCommand("weakpass") { Id = member });

        (await weak.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().OnlyContain(e => e.PropertyName == "Password");
        (await _host.GetAccessAsync(member))!.Should().BeEquivalentTo(new { SessionStamp = stampBefore, MustChangePassword = false });
        (await _host.SendAnonymousAsync(new LoginCommand("member@company.example", AccountsTestHost.Password))).AccessToken.Should().NotBeNullOrEmpty();
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NobodySetsTheirOwnTemporaryPassword_OrAWaitingSignUps()
    {
        await AdminAsync();
        var pending = await _host.AddUserAsync("waiting@company.example", Roles.Manager, AccountStatus.PendingApproval);

        var self = () => AsAdmin(new SetTemporaryPasswordCommand(Temporary) { Id = _admin });
        var waiting = () => AsAdmin(new SetTemporaryPasswordCommand(Temporary) { Id = pending });

        await self.Should().ThrowAsync<ConflictException>().WithMessage("Change your own password*");
        await waiting.Should().ThrowAsync<ConflictException>().WithMessage("*Approve or reject it instead.");
    }

    [Fact]
    public async Task ChangingYourPassword_NeedsTheCurrentOne_AndANewOne()
    {
        var member = await _host.AddUserAsync("member@company.example", Roles.Employee, mustChangePassword: true);

        var wrong = () => _host.SendAsync(member, Roles.Employee, new ChangePasswordCommand("Wrong-Pass-1", "Brand-New-Pass-7"));
        var same = () => _host.SendAsync(member, Roles.Employee, new ChangePasswordCommand(AccountsTestHost.Password, AccountsTestHost.Password));
        var weak = () => _host.SendAsync(member, Roles.Employee, new ChangePasswordCommand(AccountsTestHost.Password, "weakpass"));

        (await wrong.Should().ThrowAsync<ValidationException>()).Which.Errors.Should()
            .ContainSingle(e => e.PropertyName == "CurrentPassword" && e.ErrorMessage == ChangePasswordCommandHandler.WrongCurrentPasswordMessage);
        (await same.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "NewPassword");
        (await weak.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().OnlyContain(e => e.PropertyName == "NewPassword");
        (await _host.GetAccessAsync(member))!.MustChangePassword.Should().BeTrue("nothing was changed");
    }

    [Fact]
    public async Task AWaitingEmployee_CanBePutIntoATeam_AsTheAdminApprovesThem()
    {
        await AdminAsync();
        var manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        var otherEmployee = await _host.AddUserAsync("other@company.example", Roles.Employee);
        var waiting = await _host.AddUserAsync("ece@company.example", Roles.Employee, AccountStatus.PendingApproval);

        var intoEmployee = () => AsAdmin(new ApproveAccountCommand(otherEmployee) { Id = waiting });
        (await intoEmployee.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "ManagerId");

        var approved = await AsAdmin(new ApproveAccountCommand(manager) { Id = waiting });

        approved.Should().BeEquivalentTo(new { Status = AccountStatus.Active, ManagerId = (Guid?)manager, ManagerName = "Zeynep Kaya" });
    }
}
