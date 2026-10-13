using FluentAssertions;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.BookingRules;

// The office's hours, worked out for instants in UTC (ADR-0046).
public class OfficeWeekTests
{
    // Monday 5 October 2026; Istanbul is UTC+3 all year.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private const WeekDays Weekdays =
        WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday;

    private static readonly OfficeWeek NineToSix = new(Istanbul, new TimeOnly(9, 0), new TimeOnly(18, 0), Weekdays);

    private static DateTime Utc(DateOnly day, double hour) =>
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(hour);

    // A local time in Istanbul, as UTC.
    private static DateTime Local(DateOnly day, double hour) => Utc(day, hour - 3);

    [Theory]
    [InlineData(9, 18, true)]       // the whole office day
    [InlineData(10, 11.5, true)]
    [InlineData(8.75, 10, false)]   // starts before it opens
    [InlineData(17, 18.25, false)]  // ends after it closes
    [InlineData(17, 33, false)]     // runs into the next day
    public void ABookingIsWithinTheHours_WhenItStartsAndEndsWithinOneWorkdays(double from, double to, bool within)
    {
        NineToSix.Covers(Local(Monday, from), Local(Monday, to)).Should().Be(within);
    }

    [Fact]
    public void NotOnADayTheOfficeIsClosed()
    {
        var saturday = Monday.AddDays(5);

        NineToSix.Covers(Local(saturday, 10), Local(saturday, 11)).Should().BeFalse();
    }

    [Fact]
    public void TheHoursKeepToTheLocalClock_OnEitherSideOfAClockChange()
    {
        // London goes back an hour on Sunday 25 October 2026: 09:00 is 08:00 UTC on the Friday
        // before and 09:00 UTC on the Monday after.
        var office = NineToSix with { Zone = London };
        var friday = new DateOnly(2026, 10, 23);
        var monday = new DateOnly(2026, 10, 26);

        office.Covers(Utc(friday, 8), Utc(friday, 17)).Should().BeTrue();
        office.Covers(Utc(monday, 8), Utc(monday, 17)).Should().BeFalse();
        office.Covers(Utc(monday, 9), Utc(monday, 18)).Should().BeTrue();
    }

    [Fact]
    public void OpenAndClosedBetween_CoverTheStretchBetweenThem()
    {
        var from = Local(Monday.AddDays(4), 12);
        var to = Local(Monday.AddDays(7), 10);

        NineToSix.OpenBetween(from, to).Should().Equal(
            (from, Local(Monday.AddDays(4), 18)),
            (Local(Monday.AddDays(7), 9), to));
        NineToSix.ClosedBetween(from, to).Should().Equal((Local(Monday.AddDays(4), 18), Local(Monday.AddDays(7), 9)));
    }

    [Fact]
    public void AStretchWhollyOpen_HasNothingClosed()
    {
        NineToSix.ClosedBetween(Local(Monday, 10), Local(Monday, 11)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en-US", Weekdays, "Mon-Fri 09:00-18:00")]
    [InlineData("tr-TR", Weekdays, "Pzt-Cum 09:00-18:00")]
    [InlineData("en-US", WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, "Mon, Wed, Fri 09:00-18:00")]
    [InlineData("en-US", Weekdays | WeekDays.Sunday, "Mon-Fri, Sun 09:00-18:00")]
    public void ItsDescription_JoinsDaysInARow(string culture, WeekDays days, string expected)
    {
        using var _ = UiCulture.Use(culture);

        (NineToSix with { Days = days }).Describe().Should().Be(expected);
    }
}
