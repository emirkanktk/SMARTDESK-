using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;

// Same range limit as the reservation calendar it's drawn next to.
public sealed class GetResourceBlocksQueryValidator : AbstractValidator<GetResourceBlocksQuery>
{
    public GetResourceBlocksQueryValidator()
    {
        RuleFor(x => x.From).NotEmpty();

        RuleFor(x => x.To)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((query, to) => to.AsUtc() > query.From.AsUtc())
            .WithMessage("'To' must be after 'From'.")
            .Must((query, to) => to.AsUtc() - query.From.AsUtc() <= ReservationRules.MaxCalendarRange)
            .WithMessage($"The range can be at most {ReservationRules.MaxCalendarDays} days.");
    }
}
