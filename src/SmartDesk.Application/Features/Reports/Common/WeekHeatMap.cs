using SmartDesk.Application.Common.Extensions;

namespace SmartDesk.Application.Features.Reports.Common;

// When the resources are busiest (ADR-0044): for each weekday and local hour, the share of the
// resources' time in that hour that was booked, over the days of that weekday in the range.
// 0 to 1, Monday first.
public static class WeekHeatMap
{
    public static IReadOnlyList<WeekdayHours> Compute(
        TimeZoneInfo zone,
        DateOnly from,
        DateOnly to,
        IEnumerable<(DateTime Start, DateTime End)> booked,
        int resources)
    {
        var sums = new double[7, 24];
        var days = new int[7];

        // Every local hour of the range as a UTC window, earliest first. A daylight-saving
        // change leaves one hour empty and stretches another; both still add up to the day.
        var windows = new List<(DateTime Start, DateTime End, int Day, int Hour)>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var weekday = Index(day.DayOfWeek);
            days[weekday]++;
            for (var hour = 0; hour < 24; hour++)
            {
                var start = zone.LocalToUtc(day.ToDateTime(new TimeOnly(hour, 0)));
                var end = zone.LocalToUtc(day.ToDateTime(TimeOnly.MinValue).AddHours(hour + 1));
                if (end > start)
                {
                    windows.Add((start, end, weekday, hour));
                }
            }
        }

        foreach (var (start, end) in booked)
        {
            // The first hour that ends after the booking starts, then each one it covers.
            var low = 0;
            var high = windows.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (windows[middle].End <= start)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            for (var i = low; i < windows.Count && windows[i].Start < end; i++)
            {
                var window = windows[i];
                var overlapStart = start > window.Start ? start : window.Start;
                var overlapEnd = end < window.End ? end : window.End;
                sums[window.Day, window.Hour] += (overlapEnd - overlapStart).TotalHours;
            }
        }

        return Enumerable.Range(0, 7)
            .Select(weekday => new WeekdayHours(
                Day(weekday),
                Enumerable.Range(0, 24)
                    .Select(hour => Share(sums[weekday, hour], days[weekday] * resources))
                    .ToList()))
            .ToList();
    }

    // Monday is 0.
    private static int Index(DayOfWeek day) => ((int)day + 6) % 7;

    private static DayOfWeek Day(int index) => (DayOfWeek)((index + 1) % 7);

    // Booked hours over the resource-hours there were; a stretched hour can't make it more
    // than full.
    private static double Share(double booked, int hours) =>
        hours > 0 ? Math.Round(Math.Min(1, booked / hours), 4, MidpointRounding.AwayFromZero) : 0;
}

// One weekday of the heat map: 24 shares, from 00:00-01:00 to 23:00-24:00 local.
public sealed record WeekdayHours(DayOfWeek Day, IReadOnlyList<double> Hours);
