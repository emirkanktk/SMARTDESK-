using FluentAssertions;
using SmartDesk.Application.Features.Auth.Commands.Login;

namespace SmartDesk.Tests.Auth;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_WellFormedCommand_Passes()
    {
        var result = _validator.Validate(new LoginCommand("admin@smartdesk.local", "SmartDesk123!"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_MissingOrMalformedEmail_FailsOnEmail(string email)
    {
        var result = _validator.Validate(new LoginCommand(email, "SmartDesk123!"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void Validate_EmptyPassword_FailsOnPassword()
    {
        var result = _validator.Validate(new LoginCommand("admin@smartdesk.local", ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(LoginCommand.Password));
    }
}
