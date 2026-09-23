using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservations;

public sealed class GetReservationsQueryValidator : AbstractValidator<GetReservationsQuery>
{
    public GetReservationsQueryValidator()
    {
        RuleFor(x => x.From).NotEmpty();

        // A month view is the widest calendar screen; same bound as the availability search.
        RuleFor(x => x.To)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((query, to) => to.AsUtc() > query.From.AsUtc())
            .WithMessage("'To' must be after 'From'.")
            .Must((query, to) => to.AsUtc() - query.From.AsUtc() <= ReservationRules.MaxDuration)
            .WithMessage($"The range can be at most {ReservationRules.MaxDuration.TotalDays} days.");
    }
}
