using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Accounts.Commands.Register;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Email).AccountEmail();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);

        RuleFor(x => x.ManagerEmail!)
            .Cascade(CascadeMode.Stop)
            .EmailAddress()
            .MaximumLength(AccountRules.MaxEmailLength)
            .Must((command, managerEmail) => !string.Equals(managerEmail.Trim(), command.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage(_ => Messages.Accounts_ManagerEmailNotOwn)
            .When(x => !string.IsNullOrWhiteSpace(x.ManagerEmail));
    }
}
