using FluentValidation;

namespace SmartDesk.Application.Features.Auth.Commands.ResetPassword;

// Shape only; the password policy itself is Identity's and is reported by the handler.
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(256);
    }
}
