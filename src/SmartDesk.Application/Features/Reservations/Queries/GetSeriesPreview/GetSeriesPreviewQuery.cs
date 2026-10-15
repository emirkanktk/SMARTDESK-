using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Queries.GetSeriesPreview;

// The dates a repeating booking would have, and which of them are free (ADR-0035), so the
// booker sees them before booking. Same rules as CreateReservationSeriesCommand; nothing is
// held, so booking checks again.
public sealed record GetSeriesPreviewQuery(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    RecurrenceFrequency Frequency,
    DateOnly Until,
    string TimeZone,
    int Interval = 1,
    DayOfWeek[]? DaysOfWeek = null) : IRequest<SeriesPreviewDto>
{
    public Recurrence ToRule() => new(Frequency, Interval, DaysOfWeek, Until, TimeZone);
}

public sealed record SeriesPreviewDto(IReadOnlyList<SeriesDateDto> Dates);

public sealed class GetSeriesPreviewQueryValidator : AbstractValidator<GetSeriesPreviewQuery>
{
    public GetSeriesPreviewQueryValidator(TimeProvider timeProvider)
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
            .Must((query, end) => end.AsUtc() > query.StartDateTime.AsUtc())
            .WithMessage(_ => Messages.Validation_EndAfterStart)
            .Must((query, end) => end.AsUtc() - query.StartDateTime.AsUtc() <= ReservationRules.MaxDuration)
            .WithMessage(_ => string.Format(Messages.Reservations_MaxDuration, ReservationRules.MaxDuration.TotalDays));

        RuleFor(x => x.ToRule()).SetValidator(new RecurrenceValidator()).OverridePropertyName(nameof(Recurrence));

        RuleFor(x => x)
            .Custom((query, context) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                foreach (var (property, message) in RecurrenceRules.Problems(query.StartDateTime, query.EndDateTime, query.ToRule(), now))
                {
                    context.AddFailure(property, message);
                }
            })
            .When(x => RecurrenceRules.CanExpand(x.StartDateTime, x.EndDateTime, x.ToRule()));
    }
}

public sealed class GetSeriesPreviewQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, BookingPolicy policy)
    : IRequestHandler<GetSeriesPreviewQuery, SeriesPreviewDto>
{
    public async Task<SeriesPreviewDto> Handle(GetSeriesPreviewQuery request, CancellationToken cancellationToken)
    {
        var rule = request.ToRule();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZone);
        var start = request.StartDateTime.AsUtc();
        var days = RecurrenceRules.DaysOf(rule, RecurrenceRules.LocalDate(start, zone));
        var dates = RecurrenceRules.Occurrences(start, request.EndDateTime.AsUtc(), rule.Frequency, rule.Interval, days, rule.Until, zone);

        // As for booking: 404 for another team's resource, 409 for one out of use.
        var resource = await BookingGuard.EnsureResourceBookableAsync(db, currentUser, request.ResourceId, cancellationToken);

        // A kind that waits for an Admin's approval doesn't repeat (ADR-0049): 400, as for booking.
        await policy.EnsureCanRepeatAsync(request.ResourceId, currentUser.IsInRole(Roles.Admin), cancellationToken);

        // And as for booking, its kind's rules (ADR-0046): 400 for the first or last date, and the
        // dates outside office hours shown as not free, with why.
        var outside = await policy.CheckSeriesAsync(request.ResourceId, dates, cancellationToken);
        var taken = await BookingGuard.FindConflictsAsync(db, request.ResourceId, resource.Name, dates, null, cancellationToken);
        var conflicts = dates.Select((_, i) => outside[i] ?? taken[i]).ToList();

        return new SeriesPreviewDto(dates
            .Select((date, i) => new SeriesDateDto(date.Start, date.End, conflicts[i] is null, conflicts[i]?.Code, conflicts[i]?.Reason))
            .ToList());
    }
}
