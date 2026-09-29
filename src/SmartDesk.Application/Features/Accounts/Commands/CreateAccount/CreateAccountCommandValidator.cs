using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.Accounts.Commands.CreateAccount;

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Email).AccountEmail();
        RuleFor(x => x.Role).AccountRole();
        RuleFor(x => x.Password).TemporaryPassword();
        RuleFor(x => x.ManagerId)
            .Null()
            .When(x => x.Role != Roles.Employee)
            .WithMessage(_ => Messages.Accounts_ManagerOnlyForEmployees);
    }
}
