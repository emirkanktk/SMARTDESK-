using FluentAssertions;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.BookingRules;

// A kind's own rules, within the rules for every booking (ADR-0046).
public class BookingLimitsTests
{
    // Monday 5 October 2026, 08:00 UTC; the office keeps to UTC here, so the times read as they are.
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private static readonly OfficeWeek Office = new(
        TimeZoneInfo.Utc,
        new TimeOnly(9, 0),
        new TimeOnly(18, 0),
        WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday);

    private static readonly BookingLimits Unset = new(Guid.NewGuid(), "Desk", null, null, null, false, Office);

    private static DateTime At(int day, double hour) => Now.Date.AddDays(day).AddHours(hour);

    private static IEnumerable<(string Property, string Message)> Problems(BookingLimits limits, DateTime start, DateTime end) =>
        limits.TimeProblems(start, end, Now);

    [Fact]
    public void AKindWithNoRulesOfItsOwn_KeepsOnlyToTheRulesForEveryBooking()
    {
        Problems(Unset, At(10, 20), At(12, 7)).Should().BeEmpty();
        Problems(Unset, At(1, 9), At(33, 9)).Should().ContainSingle()
            .Which.Should().Be(("EndDateTime", "A reservation can last at most 31 days."));
        Problems(Unset, At(181, 9), At(181, 10)).Should().ContainSingle()
            .Which.Should().Be(("StartDateTime", "A reservation can start at most 180 days ahead."));
    }

    [Theory]
    [InlineData(540, 9, "9 hours")]
    [InlineData(90, 1.5, "1 hour 30 minutes")]
    [InlineData(1680, 28, "1 day 4 hours")]
    public void ABookingNoLongerThanItsKindAllows(int minutes, double hours, string described)
    {
        var limits = Unset with { MaxDurationMinutes = minutes };

        Problems(limits, At(1, 0), At(1, hours)).Should().BeEmpty();
        Problems(limits, At(1, 0), At(1, hours + 0.25)).Should().ContainSingle()
            .Which.Should().Be(("EndDateTime", $"Desk bookings can last at most {described}."));
    }

    [Fact]
    public void DaysAhead_CountsTheOfficesDays_UpToTheEndOfTheLastOne()
    {
        var limits = Unset with { MaxDaysAhead = 7 };

        Problems(limits, At(7, 23.75), At(8, 0)).Should().BeEmpty();
        Problems(limits, At(8, 0), At(8, 1)).Should().ContainSingle()
            .Which.Should().Be(("StartDateTime", "Desk bookings can start at most 7 days ahead."));
        limits.LatestStart(Now).Should().Be(At(8, 0) - TimeSpan.FromTicks(1));
    }

    [Fact]
    public void NoDaysAhead_IsTodayOnly()
    {
        var limits = Unset with { MaxDaysAhead = 0 };

        Problems(limits, At(0, 22), At(0, 23)).Should().BeEmpty();
        Problems(limits, At(1, 9), At(1, 10)).Should().ContainSingle()
            .Which.Message.Should().Be("Desk bookings can only be made for today.");
    }

    [Fact]
    public void AKindsDaysAhead_NeverGoesPastTheRuleForEveryBooking()
    {
        var limits = Unset with { MaxDaysAhead = 180 };
        var furthest = Now.AddDays(180);

        limits.LatestStart(Now).Should().Be(furthest);
        Problems(limits, furthest.AddHours(1), furthest.AddHours(2)).Should().ContainSingle()
            .Which.Message.Should().Be("A reservation can start at most 180 days ahead.");
    }

    [Fact]
    public void WithinOfficeHours_OnAWorkday()
    {
        var limits = Unset with { OfficeHoursOnly = true };

        Problems(limits, At(0, 9), At(0, 18)).Should().BeEmpty();
        Problems(limits, At(0, 17), At(0, 19)).Should().ContainSingle()
            .Which.Should().Be(("StartDateTime", "Desk bookings must be within office hours: Mon-Fri 09:00-18:00."));
        Problems(limits, At(5, 10), At(5, 11)).Should().ContainSingle(); // Saturday
    }

    [Fact]
    public void EveryRuleBroken_IsSaid()
    {
        var limits = new BookingLimits(Guid.NewGuid(), "Meeting Room", 60, 1, null, true, Office);

        Problems(limits, At(3, 17), At(3, 19)).Select(p => p.Message).Should().Equal(
            "Meeting Room bookings can last at most 1 hour.",
            "Meeting Room bookings can start at most 1 day ahead.",
            "Meeting Room bookings must be within office hours: Mon-Fri 09:00-18:00.");
    }

    [Fact]
    public void InTheRequestsLanguage_WithTheKindAsItsNamedThere()
    {
        using var _ = UiCulture.Use("tr-TR");
        var limits = Unset with { MaxDurationMinutes = 540, MaxDaysAhead = 7, OfficeHoursOnly = true };

        Problems(limits, At(8, 17), At(9, 3)).Select(p => p.Message).Should().Equal(
            "Masa rezervasyonları en fazla 9 saat sürebilir.",
            "Masa rezervasyonları en fazla 7 gün sonrası için yapılabilir.",
            "Masa rezervasyonları mesai saatleri içinde olmalı: Pzt-Cum 09:00-18:00.");
    }

    [Fact]
    public void RunningOn_StopsAtItsLengthAndAtClosingTime()
    {
        var limits = Unset with { MaxDurationMinutes = 240, OfficeHoursOnly = true };

        limits.LatestEnd(At(0, 15)).Should().Be(At(0, 18));
        limits.LatestEnd(At(0, 10)).Should().Be(At(0, 14));
        limits.RunningOnProblem(At(0, 15), At(0, 18)).Should().BeNull();
        limits.RunningOnProblem(At(0, 15), At(0, 18.5)).Should().Be("Desk bookings must be within office hours: Mon-Fri 09:00-18:00.");
        limits.RunningOnProblem(At(0, 10), At(0, 14.5)).Should().Be("Desk bookings can last at most 4 hours.");
        Unset.LatestEnd(At(0, 15)).Should().Be(At(31, 15));
    }

    [Fact]
    public void ASeries_ReachesNoFurtherThanTheKindAllows()
    {
        var limits = Unset with { MaxDaysAhead = 14 };

        limits.SeriesReachProblem(At(14, 10), Now).Should().BeNull();
        limits.SeriesReachProblem(At(21, 10), Now).Should().Be("Desk bookings can start at most 14 days ahead: end the series sooner.");
        (Unset with { MaxDaysAhead = 0 }).SeriesReachProblem(At(7, 10), Now).Should().Be("Desk bookings can only be made for today.");
    }
}
