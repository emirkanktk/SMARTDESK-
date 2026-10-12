using FluentAssertions;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Tests.Reservations;

// The nearest free times a clash offers (ADR-0042).
public class FreeTimesTests
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    // The whole day looked at, any of it bookable, unless a test says otherwise.
    private static IReadOnlyList<(DateTime Start, DateTime End)> Nearest(
        DateTime wanted,
        TimeSpan duration,
        (DateTime Start, DateTime End)[] busy,
        DateTime? earliest = null,
        DateTime? latest = null,
        DateTime? from = null,
        DateTime? to = null) =>
        FreeTimes.Nearest(
            busy, wanted, duration, from ?? Day, to ?? Day.AddDays(1), earliest ?? Day, latest ?? Day.AddDays(1), count: 3);

    [Fact]
    public void Nearest_OffersTheNearestTimeInEachGap_EarliestFirst()
    {
        // Taken 09:00-12:00 and 13:30-15:00, an hour wanted at 10:00: the hour just before, the
        // hour just after, and the next gap.
        var times = Nearest(At(10), Hour, [(At(9), At(12)), (At(13, 30), At(15))]);

        times.Should().Equal((At(8), At(9)), (At(12), At(13)), (At(15), At(16)));
    }

    [Fact]
    public void Nearest_KeepsTheNearestThree_TheLaterOneWhenTwoAreAsNear()
    {
        var busy = new[] { (At(7), At(8)), (At(9), At(10)), (At(11), At(12)), (At(13), At(14)) };

        // 08:00 and 10:00 are an hour off; 06:00 and 12:00 three hours, and 12:00 is later.
        var times = Nearest(At(9), Hour, busy);

        times.Should().Equal((At(8), At(9)), (At(10), At(11)), (At(12), At(13)));
    }

    [Fact]
    public void Nearest_ComesAsCloseToTheWantedStartAsAGapAllows()
    {
        // Taken 10:00-10:30: from 09:00 or from 10:30, not at the far ends of the day.
        var times = Nearest(At(10), Hour, [(At(10), At(10, 30))]);

        times.Should().Equal((At(9), At(10)), (At(10, 30), At(11, 30)));
    }

    [Fact]
    public void Nearest_StaysOnTheQuarterHour_AndPassesOverGapsTooShortForIt()
    {
        // 10:05-11:05 is an hour, but no hour on the quarter hour fits in it.
        var busy = new[] { (At(9), At(10, 5)), (At(11, 5), At(12, 10)) };

        var times = Nearest(At(10), Hour, busy);

        times.Should().Equal((At(8), At(9)), (At(12, 15), At(13, 15)));
    }

    [Fact]
    public void Nearest_TakesBookingsAndBlocksThatTouchOrOverlap_AsOne()
    {
        var busy = new[] { (At(9), At(10)), (At(10), At(11)), (At(10, 30), At(12)) };

        var times = Nearest(At(10), Hour, busy);

        times.Should().Equal((At(8), At(9)), (At(12), At(13)));
    }

    [Fact]
    public void Nearest_NeverOffersATimeGoneBy_OrBeyondTheLastStartAllowed()
    {
        var busy = new[] { (At(9), At(10, 30)) };

        Nearest(At(10), Hour, busy, earliest: At(10, 5)).Should().Equal((At(10, 30), At(11, 30)));
        Nearest(At(10), Hour, busy, earliest: At(10, 5), latest: At(10)).Should().BeEmpty();
        // At 07:40 the hour from 08:00 is still to come; at 08:20 it has begun.
        Nearest(At(10), Hour, busy, earliest: At(7, 40)).Should().Equal((At(8), At(9)), (At(10, 30), At(11, 30)));
        Nearest(At(10), Hour, busy, earliest: At(8, 20)).Should().Equal((At(10, 30), At(11, 30)));
    }

    [Fact]
    public void Nearest_LooksOnlyInsideTheStretchItWasGiven()
    {
        // From 08:00 to 12:00 only: after 11:30 there's half an hour, too short.
        var times = Nearest(At(9), Hour, [(At(9), At(11, 30))], from: At(8), to: At(12));

        times.Should().Equal((At(8), At(9)));
    }
}
