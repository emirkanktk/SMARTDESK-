using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.ForgotPassword;
using SmartDesk.Application.Features.Auth.Commands.ResetPassword;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// The whole reset flow on real Identity (SQLite, data protection, token provider): ask, take
// the link out of the queued email, use it.
public sealed class PasswordResetTests : IDisposable
{
    private const string Email = "employee2@smartdesk.local";
    private const string OldPassword = "SmartDesk123!";
    private const string NewPassword = "Brand-New-Pass-7";

    private readonly SqliteTestDatabase _database = new();
    private readonly CapturingEmailSender _emails = new();
    private ServiceProvider _services = null!;

    public PasswordResetTests() => BuildServices(AccountEmails.ResetLinkLifetime);

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private void BuildServices(TimeSpan tokenLifespan)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddScoped(_ => _database.CreateContext());
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = tokenLifespan);
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<IEmailSender>(_emails);
        services.AddSingleton<IFrontendLinks>(new FrontendLinks(
            Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200/" })));
        _services = services.BuildServiceProvider();
    }

    private async Task AddUserAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Email, Email = Email, FirstName = "Mehmet", LastName = "Demir" };
        (await users.CreateAsync(user, OldPassword)).Succeeded.Should().BeTrue();
    }

    private async Task ForgotAsync(string email)
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await new ForgotPasswordCommandHandler(
                sp.GetRequiredService<IIdentityService>(), sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<IFrontendLinks>())
            .Handle(new ForgotPasswordCommand(email), CancellationToken.None);
    }

    private async Task ResetAsync(string email, string token, string password)
    {
        await using var scope = _services.CreateAsyncScope();
        await new ResetPasswordCommandHandler(scope.ServiceProvider.GetRequiredService<IIdentityService>())
            .Handle(new ResetPasswordCommand(email, token, password), CancellationToken.None);
    }

    private async Task<bool> PasswordWorksAsync(string password)
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CheckPasswordAsync((await users.FindByEmailAsync(Email))!, password);
    }

    // The token as it appears in the emailed link.
    private string TokenFromEmail()
    {
        var link = _emails.Sent.Last().Text.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("http"));
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
        query["email"].Should().Be(Email);
        return query["token"]!;
    }

    [Fact]
    public async Task AKnownEmail_GetsAResetLink_ThatSetsANewPassword()
    {
        await AddUserAsync();

        await ForgotAsync("  " + Email + " ");

        var email = _emails.Sent.Should().ContainSingle().Subject;
        email.To.Should().Be(Email);
        email.Subject.Should().Be("Reset your SmartDesk password");
        email.Text.Should().Contain("Hello Mehmet,").And.Contain("http://localhost:4200/reset-password?email=");
        email.Html.Should().Contain("href=\"http://localhost:4200/reset-password?email=");

        await ResetAsync(Email, TokenFromEmail(), NewPassword);

        (await PasswordWorksAsync(NewPassword)).Should().BeTrue();
        (await PasswordWorksAsync(OldPassword)).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownEmail_QueuesNothing_AndLooksTheSameToTheCaller()
    {
        await AddUserAsync();

        await FluentActions.Awaiting(() => ForgotAsync("nobody@smartdesk.local")).Should().NotThrowAsync();

        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ALink_WorksOnlyOnce()
    {
        await AddUserAsync();
        await ForgotAsync(Email);
        var token = TokenFromEmail();
        await ResetAsync(Email, token, NewPassword);

        var again = () => ResetAsync(Email, token, "Another-Pass-9");

        (await again.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainSingle(e => e.PropertyName == "Token" && e.ErrorMessage == ResetPasswordCommandHandler.InvalidLinkMessage);
        (await PasswordWorksAsync(NewPassword)).Should().BeTrue();
    }

    [Theory]
    [InlineData("not base64url at all!")]
    [InlineData("dGFtcGVyZWQ")] // valid base64url, not a token
    public async Task ABrokenOrMadeUpToken_IsRefusedAsAnInvalidLink(string token)
    {
        await AddUserAsync();

        var act = () => ResetAsync(Email, token, NewPassword);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainSingle(e => e.PropertyName == "Token");
        (await PasswordWorksAsync(OldPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task ALink_ForOneEmail_DoesNotWorkForAnother()
    {
        await AddUserAsync();
        await ForgotAsync(Email);

        var act = () => ResetAsync("nobody@smartdesk.local", TokenFromEmail(), NewPassword);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Token");
    }

    [Fact]
    public async Task AWeakPassword_IsRefusedWithIdentitysReasons_AndTheLinkStillWorks()
    {
        await AddUserAsync();
        await ForgotAsync(Email);
        var token = TokenFromEmail();

        var weak = () => ResetAsync(Email, token, "short");

        (await weak.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().OnlyContain(e => e.PropertyName == "NewPassword").And.Contain(e => e.ErrorMessage.Contains("at least"));
        await ResetAsync(Email, token, NewPassword); // the failed try didn't use the link up
        (await PasswordWorksAsync(NewPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task AnExpiredLink_IsRefused()
    {
        _services.Dispose();
        BuildServices(TimeSpan.FromMilliseconds(1));
        await AddUserAsync();
        await ForgotAsync(Email);
        await Task.Delay(50);

        var act = () => ResetAsync(Email, TokenFromEmail(), NewPassword);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "Token");
    }

    [Fact]
    public async Task AReset_EndsALockout()
    {
        await AddUserAsync();
        await using (var scope = _services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
        }
        await ForgotAsync(Email);

        await ResetAsync(Email, TokenFromEmail(), NewPassword);

        await using (var scope = _services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.IsLockedOutAsync((await users.FindByEmailAsync(Email))!)).Should().BeFalse();
        }
    }

    [Fact]
    public void Validators_CheckTheShapeOfEachRequest()
    {
        new ForgotPasswordCommandValidator().Validate(new ForgotPasswordCommand("not-an-email"))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Email");
        new ResetPasswordCommandValidator().Validate(new ResetPasswordCommand(Email, "", ""))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Token", "NewPassword");
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public void Queue(EmailMessage message) => Sent.Add(message);
    }
}
