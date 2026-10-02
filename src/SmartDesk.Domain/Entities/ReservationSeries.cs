using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// A booking that repeats (ADR-0035): the rule it was made with. Each of its dates is a
// Reservation of its own pointing here, so everything a booking does - the checks, reminders,
// check-in, invitations - works for each date as for any other, and one date can be moved or
// cancelled alone. The rule never changes once made; cancelling the rest of a series cancels
// those dates and leaves the rule as it was.
public class ReservationSeries
{
    public Guid Id { get; set; }

    // Who made it: the owner of every date.
    public Guid UserId { get; set; }

    public RecurrenceFrequency Frequency { get; set; }

    // Every this many days, or weeks.
    public int Interval { get; set; }

    // The days a weekly series is on; None for a daily one.
    public WeekDays DaysOfWeek { get; set; }

    // The last day it may come back on, in its time zone, inclusive.
    public DateOnly Until { get; set; }

    // The IANA time zone its dates were worked out in, so they keep their local time when the
    // clocks change.
    public string TimeZone { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
