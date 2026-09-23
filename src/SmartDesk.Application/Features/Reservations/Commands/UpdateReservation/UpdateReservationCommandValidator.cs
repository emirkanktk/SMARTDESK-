using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;

// Shape rules only. "Start not in the past" depends on whether the reservation has already
// started (then the start is simply kept), so the handler checks it against the stored row.
public sealed class UpdateReservationCommandValidator : AbstractValidator<UpdateReservationCommand>
{
    public UpdateReservationCommandValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.StartDateTime).NotEmpty();

        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((command, end) => end.AsUtc() > command.StartDateTime.AsUtc())
            .WithMessage("'End Date Time' must be after 'Start Date Time'.")
            .Must((command, end) => end.AsUtc() - command.StartDateTime.AsUtc() <= ReservationRules.MaxDuration)
            .WithMessage($"A reservation can last at most {ReservationRules.MaxDuration.TotalDays} days.");

        RuleFor(x => x.Note).MaximumLength(ReservationRules.MaxNoteLength);
    }
}
