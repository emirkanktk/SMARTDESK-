using FluentAssertions;
using FluentValidation;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;
using SmartDesk.Application.Features.Accounts.Commands.RemoveTeamMember;
using SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;
using SmartDesk.Application.Features.Accounts.Commands.RejectAccount;
using SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Accounts.Queries.GetMyTeam;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Accounts;

// A Manager and their own team - and nobody else's.
public sealed class TeamTests : IDisposable
{
    private const string Temporary = "Temp-Pass-2026";

    private readonly AccountsTestHost _host = new();
    private Guid _manager;
    private Guid _otherManager;
    private Guid _member;
    private Guid _otherTeamsMember;

    public void Dispose() => _host.Dispose();

    private async Task TwoTeamsAsync()
    {
        _manager = await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", lastName: "Kaya");
        _otherManager = await _host.AddUserAsync("okan@company.example", Roles.Manager);
        _member = await _host.AddUserAsync("mehmet@company.example", Roles.Employee, managerId: _manager, firstName: "Mehmet", lastName: "Demir");
        _otherTeamsMember = await _host.AddUserAsync("selin@company.example", Roles.Employee, managerId: _otherManager);
    }

    private Task<T> AsManager<T>(MediatR.IRequest<T> request) => _host.SendAsync(_manager, Roles.Manager, request);

    private Task AsManager(MediatR.IRequest request) => _host.SendAsync(_manager, Roles.Manager, request);

    [Fact]
    public async Task MyTeam_ListsOnlyTheCallersOwnMembers_ByName()
    {
        await TwoTeamsAsync();
        await _host.AddUserAsync("ayse@company.example", Roles.Employee, managerId: _manager, firstName: "Ayşe", lastName: "Yılmaz");
        await _host.AddUserAsync("teamless@company.example", Roles.Employee);

        var team = await AsManager(new GetMyTeamQuery());

        team.Select(a => a.Email).Should().Equal("ayse@company.example", "mehmet@company.example");
        team.Should().OnlyContain(a => a.ManagerName == "Zeynep Kaya");
    }

    [Fact]
    public async Task AddingAMember_CreatesAnEmployeeInTheCallersTeam_WithATemporaryPassword()
    {
        await TwoTeamsAsync();

        var added = await AsManager(new AddTeamMemberCommand("Elif", "Şahin", "elif@company.example", Temporary));

        added.Should().BeEquivalentTo(new { Role = Roles.Employee, Status = AccountStatus.Active, ManagerId = (Guid?)_manager, MustChangePassword = true });
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.To.Should().Be("elif@company.example");
        email.Text.Should().Contain("Zeynep Kaya created a SmartDesk account for you").And.NotContain(Temporary);
        (await AsManager(new GetMyTeamQuery())).Should().Contain(a => a.Id == added.Id);
        (await _host.SendAnonymousAsync(new LoginCommand("elif@company.example", Temporary))).PasswordChangeRequired.Should().BeTrue();
    }

    [Fact]
    public async Task AddingAMember_WithATakenEmail_OrBadDetails_IsRefused()
    {
        await TwoTeamsAsync();

        var taken = () => AsManager(new AddTeamMemberCommand("Selin", "Again", "selin@company.example", Temporary));
        var bad = () => AsManager(new AddTeamMemberCommand("", "Şahin", "elif@company.example", Temporary));
        var weak = () => AsManager(new AddTeamMemberCommand("Elif", "Şahin", "elif@company.example", "weakpass"));

        await taken.Should().ThrowAsync<ConflictException>().WithMessage(AccountRules.EmailTaken);
        await bad.Should().ThrowAsync<ValidationException>();
        (await weak.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().OnlyContain(e => e.PropertyName == "Password");
        (await _host.FindAsync(_otherTeamsMember))!.ManagerId.Should().Be(_otherManager, "the existing account wasn't moved");
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AManager_CanSwitchTheirOwnMembersOffAndOn()
    {
        await TwoTeamsAsync();

        (await AsManager(new SetAccountStatusCommand(_member, Enabled: false))).Status.Should().Be(AccountStatus.Disabled);
        (await _host.IsCurrentAsync(_member, Roles.Employee)).Should().BeFalse();

        (await AsManager(new SetAccountStatusCommand(_member, Enabled: true))).Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public async Task AnyoneOutsideTheTeam_IsNotFoundForTheManager()
    {
        await TwoTeamsAsync();
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);

        foreach (var outsider in new[] { _otherTeamsMember, _otherManager, admin, _manager, Guid.NewGuid() })
        {
            var disable = () => AsManager(new SetAccountStatusCommand(outsider, Enabled: false));
            var reset = () => AsManager(new SetTemporaryPasswordCommand(Temporary) { Id = outsider });
            var approve = () => AsManager(new ApproveAccountCommand { Id = outsider });
            var remove = () => AsManager(new RemoveTeamMemberCommand(outsider));

            await disable.Should().ThrowAsync<NotFoundException>();
            await reset.Should().ThrowAsync<NotFoundException>();
            await approve.Should().ThrowAsync<NotFoundException>();
            await remove.Should().ThrowAsync<NotFoundException>();
        }

        (await _host.FindAsync(_otherTeamsMember))!.Should().BeEquivalentTo(new { Status = AccountStatus.Active, ManagerId = (Guid?)_otherManager });
        (await _host.FindAsync(admin))!.Status.Should().Be(AccountStatus.Active);
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovingAMember_KeepsTheirAccount_OutsideAnyTeam()
    {
        await TwoTeamsAsync();

        await AsManager(new RemoveTeamMemberCommand(_member));

        (await _host.FindAsync(_member)).Should().BeEquivalentTo(new { Status = AccountStatus.Active, ManagerId = (Guid?)null });
        (await AsManager(new GetMyTeamQuery())).Should().BeEmpty();
    }

    [Fact]
    public async Task AManager_CanGiveTheirMemberATemporaryPassword()
    {
        await TwoTeamsAsync();

        var updated = await AsManager(new SetTemporaryPasswordCommand(Temporary) { Id = _member });

        updated.MustChangePassword.Should().BeTrue();
        _host.RealtimeSessions.Ended.Should().Contain(_member);
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        (email.To, email.Subject).Should().Be(("mehmet@company.example", "Your SmartDesk password was changed"));
        email.Text.Should().StartWith("Hello Mehmet,").And.Contain("Zeynep Kaya set a temporary password");
    }

    [Fact]
    public async Task AManager_ApprovesOrRejectsRequestsToJoinTheirTeam_AndNobodyElses()
    {
        await TwoTeamsAsync();
        // Named to sort after the member (Mehmet), so only "requests first" puts them on top.
        var ece = await _host.AddUserAsync("ece@company.example", Roles.Employee, AccountStatus.PendingApproval, managerId: _manager, firstName: "Umut");
        var can = await _host.AddUserAsync("can@company.example", Roles.Employee, AccountStatus.PendingApproval, managerId: _manager, firstName: "Tuna");
        var otherTeams = await _host.AddUserAsync("ali@company.example", Roles.Employee, AccountStatus.PendingApproval, managerId: _otherManager);
        var waitingManager = await _host.AddUserAsync("yusuf@company.example", Roles.Manager, AccountStatus.PendingApproval);

        var team = await AsManager(new GetMyTeamQuery());
        team.Select(a => a.FirstName).Should().Equal("Tuna", "Umut", "Mehmet");
        team.Should().NotContain(a => a.Id == otherTeams || a.Id == waitingManager);

        var intoAnotherTeam = () => AsManager(new ApproveAccountCommand(_otherManager) { Id = ece });
        await intoAnotherTeam.Should().ThrowAsync<ForbiddenAccessException>();
        var removeInsteadOfReject = () => AsManager(new RemoveTeamMemberCommand(can));
        await removeInsteadOfReject.Should().ThrowAsync<ConflictException>().WithMessage("*Approve or reject it instead.");

        (await AsManager(new ApproveAccountCommand { Id = ece })).Should().BeEquivalentTo(new { Status = AccountStatus.Active, ManagerId = (Guid?)_manager });
        await AsManager(new RejectAccountCommand(can));

        (await _host.FindAsync(can)).Should().BeNull();
        _host.Emails.Sent.Select(e => (e.To, e.Subject)).Should().BeEquivalentTo(new[]
        {
            ("ece@company.example", "Your SmartDesk account is ready"),
            ("can@company.example", "Your SmartDesk sign-up"),
        });
        foreach (var notMine in new[] { otherTeams, waitingManager })
        {
            var approve = () => AsManager(new ApproveAccountCommand { Id = notMine });
            var reject = () => AsManager(new RejectAccountCommand(notMine));
            await approve.Should().ThrowAsync<NotFoundException>();
            await reject.Should().ThrowAsync<NotFoundException>();
        }
    }
}
