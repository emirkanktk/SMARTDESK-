using System.Diagnostics;
using FluentAssertions;
using FluentValidation;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;
using SmartDesk.Application.Features.Accounts.Commands.Register;
using SmartDesk.Application.Features.Accounts.Commands.RejectAccount;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Accounts;

// Public sign-up of a Manager, the Admin's approval, and what signing in says meanwhile.
public sealed class RegistrationTests : IDisposable
{
    private const string Applicant = "zeynep@company.example";
    private const string StrongPassword = "Team-Lead-Pass-4";

    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static RegisterCommand SignUp(string email = Applicant, string password = StrongPassword) =>
        new("Zeynep", "Kaya", email, password);

    private Task<LoginResponse> LoginAsync(string email, string password) =>
        _host.SendAnonymousAsync(new LoginCommand(email, password));

    [Fact]
    public async Task ANewSignUp_IsAManagerWaitingForApproval_AndActiveAdminsHearAboutIt()
    {
        await _host.AddUserAsync("admin@company.example", Roles.Admin, firstName: "Ada");
        await _host.AddUserAsync("off-admin@company.example", Roles.Admin, AccountStatus.Disabled);

        await _host.SendAnonymousAsync(SignUp(" " + Applicant + " "));

        var account = (await _host.FindByEmailAsync(Applicant))!;
        account.Should().BeEquivalentTo(new { Role = Roles.Manager, Status = AccountStatus.PendingApproval, ManagerId = (Guid?)null, FirstName = "Zeynep" });
        _host.Emails.Sent.Select(e => (e.To, e.Subject)).Should().BeEquivalentTo(new[]
        {
            (Applicant, "We've received your SmartDesk sign-up"),
            ("admin@company.example", "New manager sign-up: Zeynep Kaya"),
        });
        var toAdmin = _host.Emails.Sent[1];
        toAdmin.Text.Should().StartWith("Hello Ada,").And.Contain("Zeynep Kaya (zeynep@company.example)");
        CapturingEmailSender.LinkIn(toAdmin, "http").Should().Be("http://localhost:4200/admin/users");
    }

    [Fact]
    public async Task ASignUpWithATakenEmail_ChangesNothing_AndOnlyTellsTheOwner()
    {
        var owner = await _host.AddUserAsync(Applicant, Roles.Employee, firstName: "Ayşe");
        await _host.AddUserAsync("admin@company.example", Roles.Admin);

        // Same answer as a new sign-up: no exception, nothing to tell them apart by.
        await _host.SendAnonymousAsync(SignUp());

        (await _host.FindAsync(owner)).Should().BeEquivalentTo(new { Role = Roles.Employee, Status = AccountStatus.Active, FirstName = "Ayşe" });
        (await LoginAsync(Applicant, AccountsTestHost.Password)).AccessToken.Should().NotBeNullOrEmpty();
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.To.Should().Be(Applicant);
        email.Subject.Should().Be("You already have a SmartDesk account");
        CapturingEmailSender.LinkIn(email, "http://localhost:4200/login").Should().NotBeNull();
        CapturingEmailSender.LinkIn(email, "http://localhost:4200/forgot-password").Should().NotBeNull();
    }

    [Fact]
    public async Task AWeakPassword_IsRefusedTheSameWay_WhetherOrNotTheEmailIsTaken()
    {
        await _host.AddUserAsync(Applicant, Roles.Employee);

        var taken = () => _host.SendAnonymousAsync(SignUp(password: "weakpass"));
        var free = () => _host.SendAnonymousAsync(SignUp(email: "free@company.example", password: "weakpass"));

        var takenErrors = (await taken.Should().ThrowAsync<ValidationException>()).Which.Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ToList();
        var freeErrors = (await free.Should().ThrowAsync<ValidationException>()).Which.Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ToList();
        takenErrors.Should().NotBeEmpty().And.OnlyContain(e => e.PropertyName == "Password").And.BeEquivalentTo(freeErrors);
        _host.Emails.Sent.Should().BeEmpty();
        (await _host.FindByEmailAsync("free@company.example")).Should().BeNull();
    }

    [Fact]
    public async Task ANewAndATakenEmail_BothTakeAtLeastTheMinimumTime()
    {
        await _host.AddUserAsync("taken@company.example", Roles.Employee);

        var taken = Stopwatch.StartNew();
        await _host.SendAnonymousAsync(SignUp("taken@company.example"));
        taken.Stop();
        var fresh = Stopwatch.StartNew();
        await _host.SendAnonymousAsync(SignUp());
        fresh.Stop();

        // A little under, for the timer's resolution.
        var floor = RegisterCommandHandler.MinimumDuration - TimeSpan.FromMilliseconds(20);
        taken.Elapsed.Should().BeGreaterThanOrEqualTo(floor);
        fresh.Elapsed.Should().BeGreaterThanOrEqualTo(floor);
    }

    [Theory]
    [InlineData("", "Kaya", Applicant, "FirstName")]
    [InlineData("Zeynep\r\nBcc: x@evil.example", "Kaya", Applicant, "FirstName")]
    [InlineData("Zeynep", "Kaya", "not-an-email", "Email")]
    public async Task ASignUpWithBadDetails_IsRefused(string firstName, string lastName, string email, string property)
    {
        var act = () => _host.SendAnonymousAsync(new RegisterCommand(firstName, lastName, email, StrongPassword));

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(e => e.PropertyName == property);
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SigningIn_BeforeApproval_SaysSo_OnlyWithTheRightPassword()
    {
        await _host.SendAnonymousAsync(SignUp());

        var right = () => LoginAsync(Applicant, StrongPassword);
        var wrong = () => LoginAsync(Applicant, "Wrong-Pass-123");

        await right.Should().ThrowAsync<ForbiddenAccessException>().WithMessage(LoginCommandHandler.PendingApprovalMessage);
        await wrong.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid email or password.");
    }

    [Fact]
    public async Task Approving_LetsTheManagerIn_AndEmailsThem()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        await _host.SendAnonymousAsync(SignUp());
        var pending = (await _host.FindByEmailAsync(Applicant))!;
        _host.Emails.Sent.Clear();

        var approved = await _host.SendAsync(admin, Roles.Admin, new ApproveAccountCommand(pending.Id));

        approved.Status.Should().Be(AccountStatus.Active);
        (await _host.FindAsync(pending.Id))!.Status.Should().Be(AccountStatus.Active);
        (await LoginAsync(Applicant, StrongPassword)).AccessToken.Should().NotBeNullOrEmpty();
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        (email.To, email.Subject).Should().Be((Applicant, "Your SmartDesk account is ready"));
        CapturingEmailSender.LinkIn(email, "http").Should().Be("http://localhost:4200/login");
    }

    [Fact]
    public async Task OnlyAWaitingSignUp_CanBeApprovedOrRejected()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        var active = await _host.AddUserAsync("member@company.example", Roles.Employee);

        var approve = () => _host.SendAsync(admin, Roles.Admin, new ApproveAccountCommand(active));
        var reject = () => _host.SendAsync(admin, Roles.Admin, new RejectAccountCommand(active));
        var unknown = () => _host.SendAsync(admin, Roles.Admin, new ApproveAccountCommand(Guid.NewGuid()));

        await approve.Should().ThrowAsync<ConflictException>();
        await reject.Should().ThrowAsync<ConflictException>().WithMessage("*Disable the account instead.");
        await unknown.Should().ThrowAsync<NotFoundException>();
        (await _host.FindAsync(active)).Should().NotBeNull();
        _host.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejecting_DeletesTheSignUp_SoTheEmailCanSignUpAgain()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        await _host.SendAnonymousAsync(SignUp());
        var pending = (await _host.FindByEmailAsync(Applicant))!;
        _host.Emails.Sent.Clear();

        await _host.SendAsync(admin, Roles.Admin, new RejectAccountCommand(pending.Id));

        (await _host.FindAsync(pending.Id)).Should().BeNull();
        _host.Emails.Sent.Should().ContainSingle(e => e.To == Applicant && e.Subject == "Your SmartDesk sign-up");

        await _host.SendAnonymousAsync(SignUp());
        (await _host.FindByEmailAsync(Applicant))!.Status.Should().Be(AccountStatus.PendingApproval);
    }

    [Fact]
    public async Task SigningIn_WhenDisabled_SaysSo()
    {
        await _host.AddUserAsync("member@company.example", Roles.Employee, AccountStatus.Disabled);

        var act = () => LoginAsync("member@company.example", AccountsTestHost.Password);

        await act.Should().ThrowAsync<ForbiddenAccessException>().WithMessage(LoginCommandHandler.DisabledMessage);
    }
}
