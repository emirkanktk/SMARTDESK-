using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.BookingRules.Common;

// The rules a booking of one kind of resource keeps to (ADR-0046): those an Admin has set for
// the kind, within the rules for every booking (ReservationRules), which a kind's only tighten.
// Pure, so the rules are easy to test; BookingPolicy reads them and applies them.
public sealed record BookingLimits(
    Guid TypeId,
    string TypeName,
    int? MaxDurationMinutes,
    int? MaxDaysAhead,
    int? MaxActivePerUser,
    bool OfficeHoursOnly,
    OfficeWeek Office)
{
    public const string StartProperty = "StartDateTime";
    public const string EndProperty = "EndDateTime";
    public const string UntilProperty = nameof(Recurrence) + "." + nameof(Recurrence.Until);

    public static BookingLimits Of(ResourceType type, OfficeWeek office) =>
        new(type.Id, type.Name, type.MaxDurationMinutes, type.MaxDaysAhead, type.MaxActivePerUser, type.OfficeHoursOnly, office);

    // The kind as the request's language names it: "Desk", "Masa".
    public string Kind => Languages.DescribeType(TypeName);

    public TimeSpan MaxDuration =>
        MaxDurationMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : ReservationRules.MaxDuration;

    // The latest a booking may start: no further ahead than any booking, and within the last
    // day its kind may be booked for, on the office's calendar - "7 days ahead" is up to the
    // end of the same weekday next week, "0" the end of today.
    public DateTime LatestStart(DateTime now)
    {
        var latest = now + ReservationRules.MaxAdvance;
        return LastDayEnds(now) is { } dayEnds && dayEnds - Tick < latest ? dayEnds - Tick : latest;
    }

    // The latest a booking that starts at `start` may run to: as long as its kind allows, and
    // no later than closing time when it's booked within office hours.
    public DateTime LatestEnd(DateTime start)
    {
        var latest = start + MaxDuration;
        if (OfficeHoursOnly)
        {
            var closes = Office.ClosesOn(Office.DateOf(start));
            latest = closes < latest ? closes : latest;
        }

        return latest;
    }

    // What's wrong with booking [start, end), as the field it's about and the message; nothing
    // when the rules allow it.
    public IEnumerable<(string Property, string Message)> TimeProblems(DateTime start, DateTime end, DateTime now)
    {
        if (end - start > MaxDuration)
        {
            yield return (EndProperty, TooLong);
        }

        if (start > LatestStart(now))
        {
            yield return (StartProperty, TooFarAhead(start, now, series: false));
        }

        if (OfficeHoursOnly && !Office.Covers(start, end))
        {
            yield return (StartProperty, OutsideOfficeHours);
        }
    }

    // Why a booking that starts at `start` can't run on to `end`; null when it can.
    public string? RunningOnProblem(DateTime start, DateTime end) =>
        end - start > MaxDuration ? TooLong
        : end > LatestEnd(start) ? OutsideOfficeHours
        : null;

    // Why a series whose last date starts at `lastStart` reaches too far; null when it doesn't.
    public string? SeriesReachProblem(DateTime lastStart, DateTime now) =>
        lastStart > LatestStart(now) ? TooFarAhead(lastStart, now, series: true) : null;

    public string OutsideOfficeHours => string.Format(Messages.Rules_OutsideOfficeHours, Kind, Office.Describe());

    public string LimitReached => string.Format(Messages.Rules_ActiveLimit, Kind, MaxActivePerUser);

    public string SeriesOverLimit(int wouldHave) =>
        string.Format(Messages.Rules_SeriesActiveLimit, Kind, MaxActivePerUser, wouldHave);

    private string TooLong => MaxDurationMinutes is null
        ? string.Format(Messages.Reservations_MaxDuration, ReservationRules.MaxDuration.TotalDays)
        : string.Format(Messages.Rules_TooLong, Kind, Durations.Describe(MaxDuration));

    // The kind's own rule when that's the one broken, the rule for every booking otherwise.
    private string TooFarAhead(DateTime start, DateTime now, bool series)
    {
        if (LastDayEnds(now) is not { } dayEnds || start < dayEnds)
        {
            return series
                ? string.Format(Messages.Series_TooFarAhead, ReservationRules.MaxAdvanceDays)
                : string.Format(Messages.Reservations_TooFarAhead, ReservationRules.MaxAdvanceDays);
        }

        var days = MaxDaysAhead!.Value;
        return days == 0 ? string.Format(Messages.Rules_TodayOnly, Kind)
            : series ? string.Format(Messages.Rules_SeriesTooFarAhead, Kind, Durations.Days(days))
            : string.Format(Messages.Rules_TooFarAhead, Kind, Durations.Days(days));
    }

    // When the last day the kind may be booked for ends, in UTC; null when the kind has no
    // rule of its own for it.
    private DateTime? LastDayEnds(DateTime now) =>
        MaxDaysAhead is { } days ? Office.MidnightOf(Office.DateOf(now).AddDays(days + 1)) : null;

    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);
}
