using FluentAssertions;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Auth;

public class LoginCommandHandlerTests
{
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();

    private LoginCommandHandler CreateHandler() => new(_identityService.Object, _tokenGenerator.Object);

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsGeneratedToken()
    {
        var user = new UserProfile(Guid.NewGuid(), "Ayşe", "Yılmaz", "ayse@smartdesk.local", [Roles.Employee]);
        var expiresAt = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        _identityService
            .Setup(s => s.CheckCredentialsAsync("ayse@smartdesk.local", "Secret123!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenGenerator.Setup(g => g.Generate(user)).Returns(new AccessToken("jwt-value", expiresAt));

        var response = await CreateHandler().Handle(new LoginCommand("ayse@smartdesk.local", "Secret123!"), CancellationToken.None);

        response.AccessToken.Should().Be("jwt-value");
        response.ExpiresAt.Should().Be(expiresAt);
        response.PasswordChangeRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TemporaryPassword_SaysItMustBeChanged()
    {
        var user = new UserProfile(Guid.NewGuid(), "Elif", "Şahin", "elif@smartdesk.local", [Roles.Employee]) { MustChangePassword = true };
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenGenerator.Setup(g => g.Generate(user)).Returns(new AccessToken("jwt-value", DateTime.UtcNow.AddHours(1)));

        var response = await CreateHandler().Handle(new LoginCommand("elif@smartdesk.local", "Temp-Pass-2026"), CancellationToken.None);

        response.PasswordChangeRequired.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccountStatus.PendingApproval)]
    [InlineData(AccountStatus.Disabled)]
    public async Task Handle_AccountThatIsntActive_ThrowsForbiddenAndIssuesNoToken(AccountStatus status)
    {
        var message = status == AccountStatus.Disabled ? LoginCommandHandler.DisabledMessage : LoginCommandHandler.PendingApprovalMessage;
        var user = new UserProfile(Guid.NewGuid(), "Zeynep", "Kaya", "zeynep@smartdesk.local", [Roles.Manager]) { Status = status };
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => CreateHandler().Handle(new LoginCommand("zeynep@smartdesk.local", "Secret123!"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>().WithMessage(message);
        _tokenGenerator.Verify(g => g.Generate(It.IsAny<UserProfile>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidCredentials_ThrowsUnauthorizedAndIssuesNoToken()
    {
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        var act = () => CreateHandler().Handle(new LoginCommand("ayse@smartdesk.local", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid email or password.");
        _tokenGenerator.Verify(g => g.Generate(It.IsAny<UserProfile>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RemembersTheLanguageTheUserSignedInWith()
    {
        var user = new UserProfile(Guid.NewGuid(), "Ayşe", "Yılmaz", "ayse@smartdesk.local", [Roles.Employee]) { Language = "en" };
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenGenerator.Setup(g => g.Generate(user)).Returns(new AccessToken("jwt-value", DateTime.UtcNow.AddHours(1)));

        using (UiCulture.Use("tr-TR"))
        {
            await CreateHandler().Handle(new LoginCommand("ayse@smartdesk.local", "Secret123!"), CancellationToken.None);
        }

        _identityService.Verify(s => s.RememberLanguageAsync(user.Id, "tr", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LeavesAnUnchangedLanguageAlone()
    {
        var user = new UserProfile(Guid.NewGuid(), "Ayşe", "Yılmaz", "ayse@smartdesk.local", [Roles.Employee]) { Language = "en" };
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenGenerator.Setup(g => g.Generate(user)).Returns(new AccessToken("jwt-value", DateTime.UtcNow.AddHours(1)));

        await CreateHandler().Handle(new LoginCommand("ayse@smartdesk.local", "Secret123!"), CancellationToken.None);

        _identityService.Verify(s => s.RememberLanguageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RefusesInTheRequestsLanguage()
    {
        _identityService
            .Setup(s => s.CheckCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);
        using var turkish = UiCulture.Use("tr");

        var act = () => CreateHandler().Handle(new LoginCommand("ayse@smartdesk.local", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("E-posta veya parola hatalı.");
    }
}
