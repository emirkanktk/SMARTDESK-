using FluentValidation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;

public sealed class ExtendReservationCommandValidator : AbstractValidator<ExtendReservationCommand>
{
    public ExtendReservationCommandValidator()
    {
        RuleFor(x => x.Minutes)
            .Must(ExtensionRules.IsOption)
            .WithMessage(_ => string.Format(Messages.Reservations_ExtensionOptions, string.Join(", ", ExtensionRules.Options)));
    }
}
