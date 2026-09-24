using FluentAssertions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Tests.Domain;

public class ReservationOverlapTests
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double hour) => Day.AddHours(hour);

    [Theory]
    [InlineData(10, 11.5, 11, 12, true)]   // the brief's example
    [InlineData(10, 11.5, 11.5, 12, false)] // back-to-back: half-open ranges
    [InlineData(10, 11.5, 9, 10, false)]    // back-to-back on the other side
    [InlineData(10, 11.5, 10.25, 10.75, true)]
    [InlineData(10, 11.5, 9, 13, true)]
    [InlineData(10, 11.5, 12, 13, false)]
    public void Overlaps_FollowsHalfOpenIntervalRule(double startA, double endA, double startB, double endB, bool expected)
    {
        ReservationOverlap.Overlaps(At(startA), At(endA), At(startB), At(endB)).Should().Be(expected);
        ReservationOverlap.Overlaps(At(startB), At(endB), At(startA), At(endA)).Should().Be(expected, "overlap is symmetric");
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public void ConfirmedOverlapping_OnlyConfirmedReservationsBlock(ReservationStatus status, bool expected)
    {
        var reservation = new Reservation { StartDateTime = At(10), EndDateTime = At(11.5), Status = status };

        var predicate = ReservationOverlap.ConfirmedOverlapping(At(11), At(12)).Compile();

        predicate(reservation).Should().Be(expected);
    }
}
