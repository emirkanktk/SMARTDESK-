using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.BookingRules.Commands.UpdateOfficeHours;

// An Admin sets when the office is open (ADR-0046). Bookings already made stay as they are; the
// hours apply to what's booked, moved or extended from now on, and to the usage report.
public sealed record UpdateOfficeHoursCommand(
    string TimeZone,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    IReadOnlyList<DayOfWeek> Workdays) : IRequest<OfficeHoursDto>;

public sealed class UpdateOfficeHoursCommandValidator : AbstractValidator<UpdateOfficeHoursCommand>
{
    public UpdateOfficeHoursCommandValidator()
    {
        RuleFor(x => x.TimeZone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);

        RuleFor(x => x.OpensAt)
            .Must(RuleBounds.IsOnQuarterHour)
            .WithMessage(_ => Messages.OfficeHours_QuarterHours);

        RuleFor(x => x.ClosesAt)
            .Cascade(CascadeMode.Stop)
            .Must(RuleBounds.IsOnQuarterHour)
            .WithMessage(_ => Messages.OfficeHours_QuarterHours)
            .Must((command, closes) => closes > command.OpensAt)
            .WithMessage(_ => Messages.OfficeHours_ClosesAfterOpens);

        RuleFor(x => x.Workdays)
            .Cascade(CascadeMode.Stop)
            .Must(days => days is { Count: > 0 })
            .WithMessage(_ => Messages.OfficeHours_NoWorkdays)
            .Must(days => days.All(day => Enum.IsDefined(day)))
            .WithMessage(_ => Messages.Series_UnknownDay);
    }
}

public sealed class UpdateOfficeHoursCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateOfficeHoursCommand, OfficeHoursDto>
{
    public async Task<OfficeHoursDto> Handle(UpdateOfficeHoursCommand request, CancellationToken cancellationToken)
    {
        var hours = await db.OfficeHours.SingleAsync(cancellationToken);
        hours.TimeZone = request.TimeZone;
        hours.OpensAt = request.OpensAt;
        hours.ClosesAt = request.ClosesAt;
        hours.Workdays = RecurrenceRules.ToWeekDays(request.Workdays);
        await db.SaveChangesAsync(cancellationToken);

        return OfficeHoursDto.From(hours);
    }
}
