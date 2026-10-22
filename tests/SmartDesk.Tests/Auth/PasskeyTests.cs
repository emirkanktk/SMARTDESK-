using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Passkeys.Commands.AddPasskey;
using SmartDesk.Application.Features.Passkeys.Commands.PasskeySignIn;
using SmartDesk.Application.Features.Passkeys.Commands.RemovePasskey;
using SmartDesk.Application.Features.Passkeys.Commands.StartPasskeyCreation;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

// The rules around Identity's WebAuthn checks (ADR-0057): the password before adding a passkey, the
// most an account keeps, what each way a check fails says, a sign-in's account checks, and where and
// how often a challenge may be answered. The ceremonies themselves run end to end in the
// integration tests (PasskeyTests there), with an authenticator made up for them.
public class PasskeyTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly JsonElement Credential = JsonSerializer.Deserialize<JsonElement>("""{"id":"AQID","type":"public-key"}""");

    private readonly UserProfile _ayse = new(UserId, "Ayşe", "Yılmaz", "ayse@smartdesk.local", [Roles.Employee])
    {
        SessionStamp = "stamp-1",
        Language = "en",
    };

    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<IPasskeys> _passkeys = new();
    private readonly Mock<IJwtTokenGenerator> _tokens = new();
    private readonly Mock<IUserSessions> _sessions = new();
    private readonly FakeCurrentUser _currentUser = new(UserId);

    public PasskeyTests()
    {
        _identity.Setup(i => i.FindByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_ayse);
        _passkeys.Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _passkeys
            .Setup(p => p.CreationOptionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyChallenge("""{"challenge":"abc"}""", "token-1"));
        _sessions
            .Setup(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedSession(Guid.NewGuid(), "refresh-token", DateTime.UtcNow.AddDays(14)));
        _tokens
            .Setup(t => t.Generate(It.IsAny<UserProfile>(), It.IsAny<Guid>()))
            .Returns(new AccessToken("jwt-value", DateTime.UtcNow.AddMinutes(15)));
    }

    [Fact]
    public async Task Adding_one_starts_only_with_the_accounts_password()
    {
        var wrong = () => StartCreation().Handle(new StartPasskeyCreationCommand("a guess"), CancellationToken.None);

        (await wrong.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainSingle(e => e.PropertyName == "Password" && e.ErrorMessage == "The password is wrong.");
        _passkeys.Verify(p => p.CreationOptionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        _identity
            .Setup(i => i.CheckCredentialsAsync("ayse@smartdesk.local", "Secret123!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_ayse);
        var options = await StartCreation().Handle(new StartPasskeyCreationCommand("Secret123!"), CancellationToken.None);

        options.Token.Should().Be("token-1");
        options.Options.GetProperty("challenge").GetString().Should().Be("abc");
    }

    [Fact]
    public async Task An_account_with_the_most_passkeys_removes_one_first()
    {
        _identity
            .Setup(i => i.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_ayse);
        _passkeys
            .Setup(p => p.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, PasskeyRules.MaxPerUser)
                .Select(i => new PasskeyDto($"id-{i}", "Phone", DateTimeOffset.UtcNow, true))
                .ToList());

        var start = () => StartCreation().Handle(new StartPasskeyCreationCommand("Secret123!"), CancellationToken.None);
        var add = () => Add().Handle(new AddPasskeyCommand(Credential, "token-1", "Laptop"), CancellationToken.None);

        (await start.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.PasskeysFull);
        (await add.Should().ThrowAsync<ConflictException>())
            .WithMessage($"An account can have at most {PasskeyRules.MaxPerUser} passkeys. Remove one you no longer use first.");
        _passkeys.Verify(
            p => p.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(PasskeyCheck.Expired, "This passkey request has expired. Try again.")]
    [InlineData(PasskeyCheck.Failed, "The new passkey couldn't be checked, so it wasn't added. Try again.")]
    public async Task A_new_passkey_that_doesnt_check_out_isnt_added(PasskeyCheck check, string message)
    {
        _passkeys
            .Setup(p => p.AddAsync(UserId, Credential.GetRawText(), "token-1", "Laptop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyAdded(check, null));

        var act = () => Add().Handle(new AddPasskeyCommand(Credential, "token-1", "Laptop"), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors
            .Should().ContainSingle(e => e.PropertyName == "Credential" && e.ErrorMessage == message);
    }

    [Fact]
    public async Task A_new_passkey_is_kept_under_its_name_without_the_spaces_around_it()
    {
        var kept = new PasskeyDto("id-1", "Laptop", DateTimeOffset.UtcNow, false);
        _passkeys
            .Setup(p => p.AddAsync(UserId, Credential.GetRawText(), "token-1", "Laptop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyAdded(PasskeyCheck.Passed, kept));

        var passkey = await Add().Handle(new AddPasskeyCommand(Credential, "token-1", "  Laptop "), CancellationToken.None);

        passkey.Should().Be(kept);
    }

    [Theory]
    [InlineData(PasskeyCheck.Expired, "This passkey request has expired. Try again.")]
    [InlineData(PasskeyCheck.Unknown, "This passkey isn't on a SmartDesk account any more. Sign in with your password, and delete it from your device.")]
    [InlineData(PasskeyCheck.Failed, "The passkey couldn't be checked. Try again, or sign in with your password.")]
    public async Task A_sign_in_that_doesnt_check_out_says_why_and_starts_no_session(PasskeyCheck check, string message)
    {
        _passkeys
            .Setup(p => p.SignInAsync(Credential.GetRawText(), "token-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeySignIn(check, null));

        var act = () => SignIn().Handle(new PasskeySignInCommand(Credential, "token-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage(message);
        _sessions.Verify(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AccountStatus.PendingApproval)]
    [InlineData(AccountStatus.Disabled)]
    public async Task A_passkey_doesnt_let_in_an_account_that_isnt_active(AccountStatus status)
    {
        _passkeys
            .Setup(p => p.SignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeySignIn(PasskeyCheck.Passed, _ayse with { Status = status }));

        var act = () => SignIn().Handle(new PasskeySignInCommand(Credential, "token-1"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>().WithMessage(
            status == AccountStatus.Disabled ? LoginCommandHandler.DisabledMessage : LoginCommandHandler.PendingApprovalMessage);
        _sessions.Verify(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_passkey_signs_in_as_a_password_does()
    {
        // Signed in in English by someone whose emails were in Turkish: they're in English now.
        _passkeys
            .Setup(p => p.SignInAsync(Credential.GetRawText(), "token-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeySignIn(PasskeyCheck.Passed, _ayse with { Language = "tr" }));

        var signedIn = await SignIn().Handle(new PasskeySignInCommand(Credential, "token-1"), CancellationToken.None);

        signedIn.Response.AccessToken.Should().Be("jwt-value");
        signedIn.RefreshToken.Should().Be("refresh-token");
        _sessions.Verify(s => s.StartAsync(UserId, "stamp-1", It.IsAny<CancellationToken>()), Times.Once);
        _identity.Verify(i => i.RememberLanguageAsync(UserId, "en", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Removing_a_passkey_the_account_doesnt_have_is_not_found()
    {
        _passkeys.Setup(p => p.RemoveAsync(UserId, "elsewhere", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => new RemovePasskeyCommandHandler(_currentUser, _passkeys.Object)
            .Handle(new RemovePasskeyCommand("elsewhere"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Two\nlines")]
    public void A_passkey_needs_a_plain_name(string name) =>
        new AddPasskeyCommandValidator().Validate(new AddPasskeyCommand(Credential, "token-1", name)).Errors
            .Should().Contain(e => e.PropertyName == "Name");

    [Fact]
    public void A_passkeys_name_fits_its_list()
    {
        var validator = new AddPasskeyCommandValidator();

        validator.Validate(new AddPasskeyCommand(Credential, "token-1", new string('a', PasskeyRules.MaxNameLength)))
            .IsValid.Should().BeTrue();
        validator.Validate(new AddPasskeyCommand(Credential, "token-1", new string('a', PasskeyRules.MaxNameLength + 1)))
            .Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void What_the_browser_sends_back_is_its_credential_as_json_and_the_token()
    {
        var validator = new PasskeySignInCommandValidator();
        var text = JsonSerializer.Deserialize<JsonElement>("\"a credential\"");
        var huge = JsonSerializer.Deserialize<JsonElement>($$"""{"id":"{{new string('a', PasskeyRules.MaxCredentialLength)}}"}""");

        validator.Validate(new PasskeySignInCommand(Credential, "token-1")).IsValid.Should().BeTrue();
        validator.Validate(new PasskeySignInCommand(default, "token-1")).Errors.Should().Contain(e => e.PropertyName == "Credential");
        validator.Validate(new PasskeySignInCommand(text, "token-1")).Errors.Should().Contain(e => e.PropertyName == "Credential");
        validator.Validate(new PasskeySignInCommand(huge, "token-1")).Errors.Should().Contain(e => e.PropertyName == "Credential");
        validator.Validate(new PasskeySignInCommand(Credential, "")).Errors.Should().Contain(e => e.PropertyName == "Token");
    }

    [Fact]
    public void Passkeys_are_made_and_used_only_where_the_app_is_served()
    {
        var origins = PasskeyOrigins.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:FrontendUrl"] = "https://desk.example.com/",
                ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            })
            .Build());

        origins.Find("https://desk.example.com").Should().Be("https://desk.example.com");
        origins.Find("http://localhost:4200").Should().Be("http://localhost:4200");
        origins.Find("HTTP://LocalHost:4200").Should().Be("http://localhost:4200");
        origins.Find("http://desk.example.com").Should().BeNull("only over https there");
        origins.Find("http://localhost:4300").Should().BeNull();
        origins.Find("https://desk.example.com.evil.example").Should().BeNull();
        origins.Find("null").Should().BeNull("an opaque origin, such as a sandboxed frame's");
        origins.Find("").Should().BeNull();
        origins.Find(null).Should().BeNull();
    }

    [Fact]
    public void A_challenge_is_answered_once_and_forgotten_when_its_time_is_up()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
        var log = new PasskeyChallengeLog(clock);
        var goodUntil = clock.GetUtcNow() + PasskeyRules.ChallengeLifetime;

        log.TryUse("first", goodUntil).Should().BeTrue();
        log.TryUse("second", goodUntil).Should().BeTrue();
        log.TryUse("first", goodUntil).Should().BeFalse();

        // Past its time the token is refused before the log is asked, so it needn't be kept.
        clock.Advance(PasskeyRules.ChallengeLifetime);
        log.TryUse("first", clock.GetUtcNow() + PasskeyRules.ChallengeLifetime).Should().BeTrue();
    }

    private StartPasskeyCreationCommandHandler StartCreation() => new(_currentUser, _identity.Object, _passkeys.Object);

    private AddPasskeyCommandHandler Add() => new(_currentUser, _passkeys.Object);

    private PasskeySignInCommandHandler SignIn() => new(_passkeys.Object, _identity.Object, _tokens.Object, _sessions.Object);
}
