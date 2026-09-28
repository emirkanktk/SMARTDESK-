using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;

public sealed class ChangeAccountRoleCommandValidator : AbstractValidator<ChangeAccountRoleCommand>
{
    public ChangeAccountRoleCommandValidator()
    {
        RuleFor(x => x.Role).AccountRole();
    }
}
