using FluentAssertions;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Tests.Reservations;

// The dates of a repeating booking (ADR-0035), worked out in the booker's time zone.
public sealed class RecurrenceRulesTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // Monday 5 October 2026, 08:00 UTC (11:00 in Istanbul).
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    // Monday 12 October 2026, 10:00-11:00 in Istanbul (UTC+3 all year).
    private static readonly DateTime Start = new(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddHours(1);

    private static IReadOnlyList<Occurrence> Dates(
        RecurrenceFrequency frequency, int interval, WeekDays days, DateOnly until, DateTime? start = null, TimeSpan? length = null, TimeZoneInfo? zone = null)
    {
        var from = start ?? Start;
        return RecurrenceRules.Occurrences(from, from + (length ?? TimeSpan.FromHours(1)), frequency, interval, days, until, zone ?? Istanbul);
    }

    private static Recurrence Weekly(DateOnly until, params DayOfWeek[] days) =>
        new(RecurrenceFrequency.Weekly, 1, days, until, "Europe/Istanbul");

    [Fact]
    public void A_weekly_series_comes_back_on_its_day_until_its_last_date_inclusive()
    {
        var dates = Dates(RecurrenceFrequency.Weekly, 1, WeekDays.Monday, new DateOnly(2026, 11, 2));

        dates.Select(d => d.Start).Should().Equal(
            Start, Start.AddDays(7), Start.AddDays(14), Start.AddDays(21));
        dates.Should().AllSatisfy(d => (d.End - d.Start).Should().Be(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_daily_series_comes_back_every_so_many_days()
    {
        Dates(RecurrenceFrequency.Daily, 1, WeekDays.None, new DateOnly(2026, 10, 15))
            .Select(d => d.Start.Day).Should().Equal(12, 13, 14, 15);
        Dates(RecurrenceFrequency.Daily, 3, WeekDays.None, new DateOnly(2026, 10, 21))
            .Select(d => d.Start.Day).Should().Equal(12, 15, 18, 21);
    }

    [Fact]
    public void A_weekly_series_can_be_on_several_days()
    {
        Dates(RecurrenceFrequency.Weekly, 1, WeekDays.Monday | WeekDays.Thursday, new DateOnly(2026, 10, 22))
            .Select(d => d.Start.Day).Should().Equal(12, 15, 19, 22);
    }

    [Fact]
    public void Every_other_week_counts_weeks_from_monday_so_a_series_starting_midweek_keeps_its_rhythm()
    {
        // Thursday 15 October: that week, then the week of 26 October, then of 9 November.
        var thursday = new DateTime(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc);

        var dates = Dates(RecurrenceFrequency.Weekly, 2, WeekDays.Monday | WeekDays.Thursday, new DateOnly(2026, 11, 12), thursday);

        dates.Select(d => DateOnly.FromDateTime(d.Start)).Should().Equal(
            new DateOnly(2026, 10, 15),
            new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 29),
            new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 12));
    }

    [Fact]
    public void Dates_keep_their_local_time_when_the_clocks_change()
    {
        // Mondays at 10:00 in New York: summer time (UTC-4) until 1 November 2026, winter (UTC-5) after.
        var summer = new DateTime(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);

        var dates = Dates(RecurrenceFrequency.Weekly, 1, WeekDays.Monday, new DateOnly(2026, 11, 9), summer, zone: NewYork);

        dates.Select(d => d.Start).Should().Equal(
            summer,
            new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc));
        dates.Should().AllSatisfy(d => (d.End - d.Start).Should().Be(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_time_the_clocks_skip_moves_to_the_first_minute_after_it()
    {
        // 02:30 in Berlin every day; on 28 March 2027 the clocks jump from 02:00 to 03:00.
        var first = new DateTime(2027, 3, 27, 1, 30, 0, DateTimeKind.Utc);

        var dates = Dates(RecurrenceFrequency.Daily, 1, WeekDays.None, new DateOnly(2027, 3, 29), first, zone: Berlin);

        dates.Select(d => d.Start).Should().Equal(
            first,
            new DateTime(2027, 3, 28, 1, 0, 0, DateTimeKind.Utc), // 03:00 summer time
            new DateTime(2027, 3, 29, 0, 30, 0, DateTimeKind.Utc)); // 02:30 summer time
    }

    [Fact]
    public void The_first_date_is_exactly_the_booking_asked_for()
    {
        var odd = Start.AddSeconds(17);

        Dates(RecurrenceFrequency.Weekly, 1, WeekDays.Monday, new DateOnly(2026, 10, 19), odd)
            .Select(d => d.Start).Should().Equal(odd, odd.AddDays(7));
    }

    [Fact]
    public void A_weekly_series_given_no_days_is_on_the_day_it_starts()
    {
        var rule = new Recurrence(RecurrenceFrequency.Weekly, 1, null, new DateOnly(2026, 11, 2), "Europe/Istanbul");

        RecurrenceRules.DaysOf(rule, new DateOnly(2026, 10, 12)).Should().Be(WeekDays.Monday);
        RecurrenceRules.DaysOf(rule with { DaysOfWeek = [] }, new DateOnly(2026, 10, 15)).Should().Be(WeekDays.Thursday);
        RecurrenceRules.DaysOf(rule with { Frequency = RecurrenceFrequency.Daily, DaysOfWeek = [DayOfWeek.Monday] }, new DateOnly(2026, 10, 12))
            .Should().Be(WeekDays.None, "a daily series has no days");
    }

    [Fact]
    public void Days_go_to_one_number_and_back_monday_first()
    {
        var days = RecurrenceRules.ToWeekDays([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday]);

        days.Should().Be(WeekDays.Monday | WeekDays.Wednesday | WeekDays.Sunday);
        RecurrenceRules.ToDays(days).Should().Equal(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Sunday);
    }

    // --- What's wrong with a rule ---

    private static List<(string Property, string Message)> Problems(Recurrence rule, DateTime? start = null, TimeSpan? length = null)
    {
        var from = start ?? Start;
        return RecurrenceRules.Problems(from, from + (length ?? TimeSpan.FromHours(1)), rule, Now).ToList();
    }

    [Fact]
    public void A_good_rule_has_no_problems()
    {
        Problems(Weekly(new DateOnly(2026, 11, 2), DayOfWeek.Monday)).Should().BeEmpty();
        Problems(Weekly(new DateOnly(2026, 11, 2))).Should().BeEmpty();
    }

    [Fact]
    public void The_days_have_to_include_the_one_it_starts_on()
    {
        Problems(Weekly(new DateOnly(2026, 11, 2), DayOfWeek.Tuesday, DayOfWeek.Thursday))
            .Should().Equal(("Recurrence.DaysOfWeek", "The series has to include the day it starts on (Monday)."));
    }

    [Fact]
    public void It_has_to_end_after_it_starts_and_give_more_than_one_date()
    {
        Problems(Weekly(new DateOnly(2026, 10, 11)))
            .Should().Equal(("Recurrence.Until", "The series has to end on or after the day it starts."));
        // Ending on its first day is allowed; it just isn't a series.
        Problems(Weekly(new DateOnly(2026, 10, 12)))
            .Should().Equal(("Recurrence.Until", "That's only one date: pick a later end, or more days, for it to repeat."));
        Problems(Weekly(new DateOnly(2026, 10, 18)))
            .Should().Equal(("Recurrence.Until", "That's only one date: pick a later end, or more days, for it to repeat."));
        Problems(Weekly(new DateOnly(2026, 10, 19))).Should().BeEmpty();
    }

    [Fact]
    public void It_runs_no_further_ahead_than_a_booking_may_start()
    {
        // 180 days after 5 October 2026 is 3 April 2027.
        var tooFar = ("Recurrence.Until", "A series can run at most 180 days ahead: end it sooner.");

        Problems(Weekly(new DateOnly(2027, 4, 4))).Should().Equal(tooFar);
        Problems(Weekly(new DateOnly(2027, 3, 29))).Should().BeEmpty("Monday 29 March is the last date");

        // On its last allowed day, but later in that day than the limit (08:00 UTC).
        var late = new DateTime(2027, 4, 2, 9, 0, 0, DateTimeKind.Utc);
        Problems(new Recurrence(RecurrenceFrequency.Daily, 1, null, new DateOnly(2027, 4, 3), "Europe/Istanbul"), late)
            .Should().Equal(tooFar);
    }

    [Fact]
    public void Its_dates_must_not_run_into_each_other()
    {
        var daily = new Recurrence(RecurrenceFrequency.Daily, 1, null, new DateOnly(2026, 10, 15), "Europe/Istanbul");

        Problems(daily, length: TimeSpan.FromHours(25))
            .Should().Equal(("Recurrence.Interval", "Each date would run into the next: make it shorter, or repeat it less often."));
        Problems(daily, length: TimeSpan.FromHours(24)).Should().BeEmpty("back to back is fine");
    }

    [Theory]
    [InlineData(0, "Europe/Istanbul")]
    [InlineData(5, "Europe/Istanbul")]
    [InlineData(1, "Mars/Olympus_Mons")]
    [InlineData(1, "")]
    public void Without_a_usable_interval_or_time_zone_there_are_no_dates_to_work_out(int interval, string zone)
    {
        var rule = new Recurrence(RecurrenceFrequency.Weekly, interval, null, new DateOnly(2026, 11, 2), zone);

        RecurrenceRules.CanExpand(Start, End, rule).Should().BeFalse();
        RecurrenceRules.CanExpand(Start, End, rule with { Interval = 1, TimeZone = "Europe/Istanbul" }).Should().BeTrue();
        RecurrenceRules.CanExpand(End, Start, rule with { Interval = 1, TimeZone = "Europe/Istanbul" }).Should().BeFalse("it ends before it starts");
    }
}
