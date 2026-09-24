using FluentValidation;

namespace SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;

public sealed class ChangeResourceStatusCommandValidator : AbstractValidator<ChangeResourceStatusCommand>
{
    public ChangeResourceStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
