using FluentValidation;
using SmartDesk.Application.Common.Extensions;

namespace SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;

public sealed class GetAvailableResourcesQueryValidator : AbstractValidator<GetAvailableResourcesQuery>
{
    // A search window longer than this is almost certainly a client bug, and it would make
    // "available" mean "free for a whole month", which nobody asks for.
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    public GetAvailableResourcesQueryValidator()
    {
        RuleFor(x => x.StartDateTime).NotEmpty();

        // Compared as UTC: DateTime comparison ignores Kind, so a Local and a Utc value would
        // otherwise be compared by their raw clock readings.
        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((query, end) => end.AsUtc() > query.StartDateTime.AsUtc())
            .WithMessage("'End Date Time' must be after 'Start Date Time'.")
            .Must((query, end) => end.AsUtc() - query.StartDateTime.AsUtc() <= MaxWindow)
            .WithMessage($"The search window can be at most {MaxWindow.TotalDays} days.");

        RuleFor(x => x.MinCapacity).GreaterThan(0).When(x => x.MinCapacity.HasValue);
    }
}
