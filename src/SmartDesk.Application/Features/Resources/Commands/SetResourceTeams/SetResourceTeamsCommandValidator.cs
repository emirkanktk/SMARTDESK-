using FluentValidation;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourceTeams;

public sealed class SetResourceTeamsCommandValidator : AbstractValidator<SetResourceTeamsCommand>
{
    public SetResourceTeamsCommandValidator()
    {
        RuleFor(x => x.TeamIds).NotNull();
        RuleFor(x => x.TeamIds).TeamList();
    }
}
