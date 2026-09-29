using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Resources;

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
            .WithMessage(_ => Messages.Blocks_StartInPast);

        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((command, end) => end.AsUtc() > command.StartDateTime.AsUtc())
            .WithMessage(_ => Messages.Validation_EndAfterStart)
            .Must((command, end) => end.AsUtc() - command.StartDateTime.AsUtc() <= ResourceBlockRules.MaxDuration)
            .WithMessage(_ => string.Format(Messages.Blocks_MaxDuration, ResourceBlockRules.MaxDuration.TotalDays));

        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ResourceBlockRules.MaxReasonLength);
    }
}
