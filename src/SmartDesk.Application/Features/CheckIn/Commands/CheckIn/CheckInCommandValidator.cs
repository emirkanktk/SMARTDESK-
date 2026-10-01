using FluentValidation;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.CheckIn;

public sealed class CheckInCommandValidator : AbstractValidator<CheckInCommand>
{
    public CheckInCommandValidator()
    {
        // A wrong code of the right shape is a 403 from the handler, which knows the resource.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(CheckInRules.CodeLength);
    }
}
