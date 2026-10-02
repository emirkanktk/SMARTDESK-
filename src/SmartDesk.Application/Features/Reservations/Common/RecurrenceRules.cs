using System.Globalization;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Common;

// The dates of a repeating booking (ADR-0035). They're worked out in the booker's time zone, so
// a meeting at 10:00 stays at 10:00 when the clocks change, and each lasts as long as the first.
public static class RecurrenceRules
{
    // Every day, up to every fourth week: the rhythms an office works to.
    public const int MaxInterval = 4;

    // Monday first, as the week is counted for "every 2 weeks".
    public static readonly IReadOnlyList<DayOfWeek> Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static WeekDays Flag(DayOfWeek day) => (WeekDays)(1 << DaysSinceMonday(day));

    public static WeekDays ToWeekDays(IEnumerable<DayOfWeek> days) =>
        days.Aggregate(WeekDays.None, (all, day) => all | Flag(day));

    public static IReadOnlyList<DayOfWeek> ToDays(WeekDays days) =>
        Week.Where(day => days.HasFlag(Flag(day))).ToList();

    // The local date of a UTC instant in the zone.
    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    // The days a series is on: those asked for, or the day it starts on when none were; a daily
    // series has none.
    public static WeekDays DaysOf(Recurrence rule, DateOnly firstDate) =>
        rule.Frequency != RecurrenceFrequency.Weekly ? WeekDays.None
        : rule.DaysOfWeek is { Count: > 0 } days ? ToWeekDays(days)
        : Flag(firstDate.DayOfWeek);

    // Every date of the series, the first included, as [start, end) in UTC, earliest first. The
    // first is exactly the booking asked for; the others keep its local time of day. A time the
    // clocks skip moves to the first minute after it. Weeks start on Monday: "every 2 weeks" is
    // the first date's week, the one two weeks later, and so on.
    public static IReadOnlyList<Occurrence> Occurrences(
        DateTime firstStartUtc,
        DateTime firstEndUtc,
        RecurrenceFrequency frequency,
        int interval,
        WeekDays days,
        DateOnly until,
        TimeZoneInfo zone)
    {
        var firstStart = DateTime.SpecifyKind(firstStartUtc, DateTimeKind.Utc);
        var duration = firstEndUtc - firstStartUtc;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(firstStart, zone);
        var firstDate = DateOnly.FromDateTime(localStart);
        var timeOfDay = TimeOnly.FromDateTime(localStart);
        var firstWeek = firstDate.AddDays(-DaysSinceMonday(firstDate.DayOfWeek));

        var dates = new List<Occurrence>();
        for (var date = firstDate; date <= until; date = date.AddDays(1))
        {
            var onIt = frequency == RecurrenceFrequency.Daily
                ? (date.DayNumber - firstDate.DayNumber) % interval == 0
                : (date.DayNumber - firstWeek.DayNumber) / 7 % interval == 0 && days.HasFlag(Flag(date.DayOfWeek));
            if (!onIt)
            {
                continue;
            }

            var start = date == firstDate ? firstStart : zone.LocalToUtc(date.ToDateTime(timeOfDay));
            dates.Add(new Occurrence(start, start + duration));
        }

        return dates;
    }

    // Whether the rule is complete enough to work out its dates: the checks of each field
    // have passed. Until is checked against the first date first, so a series never runs
    // through years of days (see Problems).
    public static bool CanExpand(DateTime start, DateTime end, Recurrence? rule) =>
        rule is not null
        && end.AsUtc() > start.AsUtc()
        && Enum.IsDefined(rule.Frequency)
        && rule.Interval is >= 1 and <= MaxInterval
        && (rule.DaysOfWeek is null || rule.DaysOfWeek.All(day => Enum.IsDefined(day)))
        && !string.IsNullOrWhiteSpace(rule.TimeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(rule.TimeZone, out _);

    // What's wrong with a series' dates, as the field it's about and the message; nothing when
    // they're fine. Call only when CanExpand.
    public static IEnumerable<(string Property, string Message)> Problems(DateTime start, DateTime end, Recurrence rule, DateTime utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZone);
        var firstDate = LocalDate(start.AsUtc(), zone);

        if (rule.Frequency == RecurrenceFrequency.Weekly
            && rule.DaysOfWeek is { Count: > 0 } days
            && !days.Contains(firstDate.DayOfWeek))
        {
            var dayName = CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(firstDate.DayOfWeek);
            yield return (DaysProperty, string.Format(Messages.Series_DaysMustIncludeFirst, dayName));
            yield break;
        }

        if (rule.Until < firstDate)
        {
            yield return (UntilProperty, Messages.Series_UntilBeforeStart);
            yield break;
        }

        // Like any booking's start (ReservationRules.MaxAdvance): checked on the date before the
        // dates are worked out, and on the last date's time after.
        var latest = utcNow + ReservationRules.MaxAdvance;
        if (rule.Until > LocalDate(latest, zone))
        {
            yield return (UntilProperty, string.Format(Messages.Series_TooFarAhead, ReservationRules.MaxAdvanceDays));
            yield break;
        }

        var dates = Occurrences(start.AsUtc(), end.AsUtc(), rule.Frequency, rule.Interval, DaysOf(rule, firstDate), rule.Until, zone);
        if (dates.Count < 2)
        {
            yield return (UntilProperty, Messages.Series_TooFewDates);
            yield break;
        }

        if (dates[^1].Start > latest)
        {
            yield return (UntilProperty, string.Format(Messages.Series_TooFarAhead, ReservationRules.MaxAdvanceDays));
            yield break;
        }

        // Its own dates would clash with each other.
        if (dates.Zip(dates.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
        {
            yield return (nameof(Recurrence) + "." + nameof(Recurrence.Interval), Messages.Series_Overlapping);
        }
    }

    private const string DaysProperty = nameof(Recurrence) + "." + nameof(Recurrence.DaysOfWeek);
    private const string UntilProperty = nameof(Recurrence) + "." + nameof(Recurrence.Until);

    private static int DaysSinceMonday(DayOfWeek day) => ((int)day + 6) % 7;
}

// One date of a series, in UTC.
public sealed record Occurrence(DateTime Start, DateTime End);
