using FluentValidation;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;

// The first date follows the rules of any booking (CreateReservationCommandValidator); the rule
// then has to give at least one more date, none of them clashing with the next, and none
// further ahead than a booking may start.
public sealed class CreateReservationSeriesCommandValidator : AbstractValidator<CreateReservationSeriesCommand>
{
    public CreateReservationSeriesCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.ResourceId).NotEmpty();

        RuleFor(x => x.StartDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(start => start.AsUtc() >= timeProvider.GetUtcNow().UtcDateTime - ReservationRules.PastStartGrace)
            .WithMessage(_ => Messages.Reservations_StartInPast)
            .Must(start => start.AsUtc() <= timeProvider.GetUtcNow().UtcDateTime + ReservationRules.MaxAdvance)
            .WithMessage(_ => string.Format(Messages.Reservations_TooFarAhead, ReservationRules.MaxAdvanceDays));

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

        RuleFor(x => x.AttendeeIds)
            .Must(ids => ids is null || ids.Count <= MeetingRules.MaxAttendees)
            .WithMessage(_ => string.Format(Messages.Meetings_TooManyAttendees, MeetingRules.MaxAttendees));

        RuleFor(x => x.Recurrence).NotNull().SetValidator(new RecurrenceValidator());

        RuleFor(x => x)
            .Custom((command, context) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                foreach (var (property, message) in RecurrenceRules.Problems(command.StartDateTime, command.EndDateTime, command.Recurrence, now))
                {
                    context.AddFailure(property, message);
                }
            })
            .When(x => RecurrenceRules.CanExpand(x.StartDateTime, x.EndDateTime, x.Recurrence));
    }
}

// Each field of the rule on its own; Problems then checks the dates it gives.
public sealed class RecurrenceValidator : AbstractValidator<Recurrence>
{
    public RecurrenceValidator()
    {
        RuleFor(x => x.Frequency).IsInEnum();

        RuleFor(x => x.Interval)
            .InclusiveBetween(1, RecurrenceRules.MaxInterval)
            .WithMessage(_ => string.Format(Messages.Series_IntervalRange, RecurrenceRules.MaxInterval));

        RuleFor(x => x.DaysOfWeek)
            .Must(days => days is null || days.All(day => Enum.IsDefined(day)))
            .WithMessage(_ => Messages.Series_UnknownDay);

        RuleFor(x => x.TimeZone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}
