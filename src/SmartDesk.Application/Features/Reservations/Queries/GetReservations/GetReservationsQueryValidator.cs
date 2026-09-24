using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservations;

public sealed class GetReservationsQueryValidator : AbstractValidator<GetReservationsQuery>
{
    public GetReservationsQueryValidator()
    {
        RuleFor(x => x.From).NotEmpty();

        // A month grid (six weeks) is the widest calendar screen.
        RuleFor(x => x.To)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((query, to) => to.AsUtc() > query.From.AsUtc())
            .WithMessage("'To' must be after 'From'.")
            .Must((query, to) => to.AsUtc() - query.From.AsUtc() <= ReservationRules.MaxCalendarRange)
            .WithMessage($"The range can be at most {ReservationRules.MaxCalendarRange.TotalDays} days.");
    }
}
