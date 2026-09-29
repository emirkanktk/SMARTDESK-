using FluentAssertions;
using FluentValidation;
using SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;
using SmartDesk.Application.Features.Accounts.Commands.CreateAccount;
using SmartDesk.Application.Features.Accounts.Commands.Register;
using SmartDesk.Application.Features.Auth.Commands.ForgotPassword;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Localization;

// Emails are written in the recipient's language, which is the one they last signed in with;
// the request that sends one is usually someone else's (ADR-0023).
public sealed class EmailLanguageTests : IDisposable
{
    private const string StrongPassword = "Str0ng-Pass-2026";

    private readonly AccountsTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Signing_in_saves_the_language_for_later_emails()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee, firstName: "Ayşe");

        using (UiCulture.Use("tr-TR"))
        {
            await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));
        }

        (await _host.FindAsync(ayse))!.Language.Should().Be("tr");

        // An English request - someone else's, or a tool's - still writes to her in Turkish.
        await _host.SendAnonymousAsync(new ForgotPasswordCommand("ayse@company.example"));
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("SmartDesk parolanızı sıfırlayın");
        email.Text.Should().StartWith("Merhaba Ayşe,").And.Contain("2 saat içinde");
    }

    [Fact]
    public async Task Signing_in_again_in_English_switches_back()
    {
        var ayse = await _host.AddUserAsync("ayse@company.example", Roles.Employee, language: "tr");

        await _host.SendAnonymousAsync(new LoginCommand("ayse@company.example", AccountsTestHost.Password));

        (await _host.FindAsync(ayse))!.Language.Should().Be("en");
    }

    [Fact]
    public async Task A_sign_up_keeps_the_applicants_language_and_each_reader_gets_their_own()
    {
        await _host.AddUserAsync("zeynep@company.example", Roles.Manager, firstName: "Zeynep", language: "en");

        using (UiCulture.Use("tr"))
        {
            await _host.SendAnonymousAsync(new RegisterCommand("Deniz", "Arslan", "deniz@company.example", StrongPassword, "zeynep@company.example"));
        }

        (await _host.FindByEmailAsync("deniz@company.example"))!.Language.Should().Be("tr");
        var toApplicant = _host.Emails.Sent.Single(e => e.To == "deniz@company.example");
        toApplicant.Subject.Should().Be("SmartDesk kaydınızı aldık");
        var toManager = _host.Emails.Sent.Single(e => e.To == "zeynep@company.example");
        toManager.Subject.Should().Be("Deniz Arslan wants to join your team");
    }

    [Fact]
    public async Task Approval_is_written_in_the_applicants_language_not_the_approvers()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        var pending = await _host.AddUserAsync("deniz@company.example", Roles.Manager, AccountStatus.PendingApproval, firstName: "Deniz", language: "tr");

        await _host.SendAsync(admin, Roles.Admin, new ApproveAccountCommand { Id = pending });

        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("SmartDesk hesabınız hazır");
        email.Text.Should().Be("Merhaba Deniz,\n\nKaydınız onaylandı. Artık giriş yapabilirsiniz.\n\nGiriş yap:\nhttp://localhost:4200/login");
    }

    [Fact]
    public async Task English_emails_read_as_they_always_have()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin);
        var pending = await _host.AddUserAsync("deniz@company.example", Roles.Manager, AccountStatus.PendingApproval, firstName: "Deniz");

        await _host.SendAsync(admin, Roles.Admin, new ApproveAccountCommand { Id = pending });

        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("Your SmartDesk account is ready");
        email.Text.Should().Be("Hello Deniz,\n\nYour sign-up was approved. You can sign in now.\n\nSign in:\nhttp://localhost:4200/login");
        email.Html.Should().Contain("<p>Your sign-up was approved. You can sign in now.</p>");
    }

    [Fact]
    public async Task An_account_someone_adds_starts_in_their_language()
    {
        var admin = await _host.AddUserAsync("admin@company.example", Roles.Admin, firstName: "Sistem", lastName: "Yöneticisi");

        using (UiCulture.Use("tr"))
        {
            await _host.SendAsync(admin, Roles.Admin, new CreateAccountCommand("Ece", "Kaya", "ece@company.example", Roles.Employee, "Temp-Pass-2026"));
        }

        (await _host.FindByEmailAsync("ece@company.example"))!.Language.Should().Be("tr");
        var email = _host.Emails.Sent.Should().ContainSingle().Subject;
        email.Subject.Should().Be("SmartDesk'e eklendiniz");
        email.Text.Should().Contain("Sistem Yöneticisi sizin için");
    }

    [Fact]
    public async Task Identitys_password_rules_come_back_in_the_requests_language()
    {
        using var turkish = UiCulture.Use("tr");

        var act = () => _host.SendAnonymousAsync(new RegisterCommand("Deniz", "Arslan", "deniz@company.example", "short"));

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.PropertyName == "Password" && e.ErrorMessage == "Parolada en az bir rakam ('0'-'9') olmalı.");
    }
}
