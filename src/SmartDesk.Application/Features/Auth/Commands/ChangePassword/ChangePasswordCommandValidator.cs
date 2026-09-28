using FluentValidation;

namespace SmartDesk.Application.Features.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(256)
            // Otherwise a temporary password could be "changed" into itself and kept.
            .Must((command, newPassword) => newPassword != command.CurrentPassword)
            .WithMessage("Choose a password different from the current one.");
    }
}
