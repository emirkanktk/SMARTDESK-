using FluentAssertions;
using SmartDesk.Application.Features.Reports.Common;

namespace SmartDesk.Tests.Reports;

// When the resources are busiest, by weekday and local hour (ADR-0044).
public class WeekHeatMapTests
{
    // Monday 5 October 2026.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static DateTime Utc(DateOnly day, double hour) =>
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(hour);

    private static IReadOnlyList<WeekdayHours> Map(
        DateOnly from, DateOnly to, int resources, params (DateTime Start, DateTime End)[] booked) =>
        WeekHeatMap.Compute(TimeZoneInfo.Utc, from, to, booked, resources);

    [Fact]
    public void EachWeekdayHasItsHours_MondayFirst_AndABookingFillsTheHoursItCovers()
    {
        var map = Map(Monday, Monday.AddDays(6), 1, (Utc(Monday, 10), Utc(Monday, 11.5)));

        map.Select(d => d.Day).Should().Equal(
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday);
        map.Should().OnlyContain(d => d.Hours.Count == 24);
        map[0].Hours[10].Should().Be(1);
        map[0].Hours[11].Should().Be(0.5);
        map.SelectMany(d => d.Hours).Sum().Should().Be(1.5);
    }

    [Fact]
    public void AShareOfTheResources_AndOfTheDaysOfThatWeekday()
    {
        // Two rooms, two Mondays: one room booked on one Monday is a quarter of that hour.
        var map = Map(Monday, Monday.AddDays(13), 2, (Utc(Monday, 9), Utc(Monday, 10)));

        map[0].Hours[9].Should().Be(0.25);
    }

    [Fact]
    public void HoursAreLocal()
    {
        // 07:00 UTC is 10:00 in Istanbul.
        var map = WeekHeatMap.Compute(Istanbul, Monday, Monday, [(Utc(Monday, 7), Utc(Monday, 8))], 1);

        map[0].Hours[10].Should().Be(1);
        map[0].Hours[7].Should().Be(0);
    }

    [Fact]
    public void OnlyTheRangeCounts_AndABookingOverMidnightGoesToBothDays()
    {
        // Sunday 22:00 to Monday 02:00, with only the Monday in range.
        var map = Map(Monday, Monday, 1, (Utc(Monday, -2), Utc(Monday, 2)));

        map[0].Hours[0].Should().Be(1);
        map[0].Hours[1].Should().Be(1);
        map[6].Hours[22].Should().Be(0); // Sunday isn't in the range
    }

    [Fact]
    public void NoResources_OrNothingBooked_IsAllZero()
    {
        Map(Monday, Monday, 0, (Utc(Monday, 9), Utc(Monday, 10))).SelectMany(d => d.Hours).Should().OnlyContain(h => h == 0);
        Map(Monday, Monday, 3).SelectMany(d => d.Hours).Should().OnlyContain(h => h == 0);
    }

    [Fact]
    public void AnHourStretchedByTheClocksGoingBack_IsNoMoreThanFull()
    {
        // 25 October 2026 in Berlin has 25 hours; booked from local midnight to midnight.
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var sunday = new DateOnly(2026, 10, 25);
        var start = TimeZoneInfo.ConvertTimeToUtc(sunday.ToDateTime(TimeOnly.MinValue), berlin);
        var end = TimeZoneInfo.ConvertTimeToUtc(sunday.AddDays(1).ToDateTime(TimeOnly.MinValue), berlin);

        var map = WeekHeatMap.Compute(berlin, sunday, sunday, [(start, end)], 1);

        map[6].Hours.Should().OnlyContain(h => h == 1);
    }
}
