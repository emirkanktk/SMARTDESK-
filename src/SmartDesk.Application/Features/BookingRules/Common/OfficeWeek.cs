using System.Globalization;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.BookingRules.Common;

// The office's hours (ADR-0046) in a time zone, for instants in UTC. The hours keep to the local
// clock: the office opens at 09:00 on either side of a clock change. Pure, so the rules are easy
// to test.
public sealed record OfficeWeek(TimeZoneInfo Zone, TimeOnly Opens, TimeOnly Closes, WeekDays Days)
{
    // The office's hours in their own zone, or in another: the usage report reads them in the
    // zone it's asked for, as it reads its days.
    public static OfficeWeek Of(OfficeHours hours, TimeZoneInfo? zone = null) =>
        new(zone ?? TimeZoneInfo.FindSystemTimeZoneById(hours.TimeZone), hours.OpensAt, hours.ClosesAt, hours.Workdays);

    public bool IsWorkday(DayOfWeek day) => Days.HasFlag(RecurrenceRules.Flag(day));

    // When the office opens and closes on a local date, in UTC.
    public DateTime OpensOn(DateOnly date) => Zone.LocalToUtc(date.ToDateTime(Opens));

    public DateTime ClosesOn(DateOnly date) => Zone.LocalToUtc(date.ToDateTime(Closes));

    // The local date an instant falls on, and when a local date begins.
    public DateOnly DateOf(DateTime utc) => RecurrenceRules.LocalDate(utc, Zone);

    public DateTime MidnightOf(DateOnly date) => Zone.LocalToUtc(date.ToDateTime(TimeOnly.MinValue));

    // Whether [start, end) lies within the hours of one workday.
    public bool Covers(DateTime start, DateTime end)
    {
        var date = DateOf(start);
        return IsWorkday(date.DayOfWeek) && start >= OpensOn(date) && end <= ClosesOn(date);
    }

    // The hours the office is open within [from, to), earliest first.
    public IReadOnlyList<(DateTime Start, DateTime End)> OpenBetween(DateTime from, DateTime to)
    {
        var open = new List<(DateTime Start, DateTime End)>();
        for (var date = DateOf(from); date <= DateOf(to); date = date.AddDays(1))
        {
            if (!IsWorkday(date.DayOfWeek))
            {
                continue;
            }

            var start = Max(OpensOn(date), from);
            var end = Min(ClosesOn(date), to);
            if (start < end)
            {
                open.Add((start, end));
            }
        }

        return open;
    }

    // The rest of [from, to): the hours it's closed, earliest first.
    public IReadOnlyList<(DateTime Start, DateTime End)> ClosedBetween(DateTime from, DateTime to)
    {
        var closed = new List<(DateTime Start, DateTime End)>();
        var cursor = from;
        foreach (var (start, end) in OpenBetween(from, to))
        {
            if (start > cursor)
            {
                closed.Add((cursor, start));
            }

            cursor = end;
        }

        if (cursor < to)
        {
            closed.Add((cursor, to));
        }

        return closed;
    }

    // "Mon-Fri 09:00-18:00", with the days as the request's language shortens them: days in a
    // row from Monday on are a range, the others are listed.
    public string Describe()
    {
        var names = CultureInfo.CurrentUICulture.DateTimeFormat.AbbreviatedDayNames;
        var week = RecurrenceRules.Week;
        var days = new List<string>();
        for (var i = 0; i < week.Count; i++)
        {
            if (!IsWorkday(week[i]))
            {
                continue;
            }

            var last = i;
            while (last + 1 < week.Count && IsWorkday(week[last + 1]))
            {
                last++;
            }

            days.Add(last > i ? $"{names[(int)week[i]]}-{names[(int)week[last]]}" : names[(int)week[i]]);
            i = last;
        }

        var hours = $"{Opens.ToString("HH:mm", CultureInfo.InvariantCulture)}-{Closes.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        return $"{string.Join(", ", days)} {hours}";
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
