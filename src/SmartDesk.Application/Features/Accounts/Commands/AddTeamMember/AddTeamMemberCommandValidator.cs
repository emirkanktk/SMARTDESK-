using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;

public sealed class AddTeamMemberCommandValidator : AbstractValidator<AddTeamMemberCommand>
{
    public AddTeamMemberCommandValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Email).AccountEmail();
        RuleFor(x => x.Password).TemporaryPassword();
    }
}
