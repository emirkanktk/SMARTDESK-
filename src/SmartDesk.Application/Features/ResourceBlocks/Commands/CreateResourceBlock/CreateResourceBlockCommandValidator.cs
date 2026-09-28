using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

public sealed class CreateResourceBlockCommandValidator : AbstractValidator<CreateResourceBlockCommand>
{
    public CreateResourceBlockCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.ResourceId).NotEmpty();

        // Blocking the past would change nothing; same grace as a reservation's start.
        RuleFor(x => x.StartDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(start => start.AsUtc() >= timeProvider.GetUtcNow().UtcDateTime - ReservationRules.PastStartGrace)
            .WithMessage("A block can't start in the past.");

        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((command, end) => end.AsUtc() > command.StartDateTime.AsUtc())
            .WithMessage("'End Date Time' must be after 'Start Date Time'.")
            .Must((command, end) => end.AsUtc() - command.StartDateTime.AsUtc() <= ResourceBlockRules.MaxDuration)
            .WithMessage($"A block can last at most {ResourceBlockRules.MaxDuration.TotalDays} days.");

        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ResourceBlockRules.MaxReasonLength);
    }
}
