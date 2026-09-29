using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

// Shape and time rules that need no database (business rules 2 and 6). Resource status and
// overlap depend on current data and are checked by the handler.
public sealed class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.ResourceId).NotEmpty();

        RuleFor(x => x.StartDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(start => start.AsUtc() >= timeProvider.GetUtcNow().UtcDateTime - ReservationRules.PastStartGrace)
            .WithMessage(_ => Messages.Reservations_StartInPast)
            .Must(start => start.AsUtc() <= timeProvider.GetUtcNow().UtcDateTime + ReservationRules.MaxAdvance)
            .WithMessage(_ => string.Format(Messages.Reservations_TooFarAhead, ReservationRules.MaxAdvanceDays));

        // Compared as UTC: DateTime comparison ignores Kind.
        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((command, end) => end.AsUtc() > command.StartDateTime.AsUtc())
            .WithMessage(_ => Messages.Validation_EndAfterStart)
            .Must((command, end) => end.AsUtc() - command.StartDateTime.AsUtc() <= ReservationRules.MaxDuration)
            .WithMessage(_ => string.Format(Messages.Reservations_MaxDuration, ReservationRules.MaxDuration.TotalDays));

        RuleFor(x => x.Note).MaximumLength(ReservationRules.MaxNoteLength);

        RuleFor(x => x.ReminderMinutes)
            .Must(ReservationRules.IsValidReminder)
            .WithMessage(_ => ReservationRules.ReminderMessage);
    }
}
