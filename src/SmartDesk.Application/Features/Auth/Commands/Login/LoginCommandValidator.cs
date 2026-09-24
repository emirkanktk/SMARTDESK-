using FluentValidation;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Stop at the first failure so an empty email reports "must not be empty", not that
        // plus "is not a valid email address".
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}
