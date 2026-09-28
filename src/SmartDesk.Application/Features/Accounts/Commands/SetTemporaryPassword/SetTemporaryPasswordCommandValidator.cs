using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;

public sealed class SetTemporaryPasswordCommandValidator : AbstractValidator<SetTemporaryPasswordCommand>
{
    public SetTemporaryPasswordCommandValidator()
    {
        RuleFor(x => x.Password).TemporaryPassword();
    }
}
