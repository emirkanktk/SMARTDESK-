namespace SmartDesk.Application.Features.Reports.Common;

// ADR-0017: what the usage report measures and over which hours.
public static class ReportRules
{
    // A quarter: enough for a quarterly review, small enough to compute on each request.
    public const int MaxRangeDays = 92;

    // Utilization is booked time within these hours, in the caller's time zone. A desk booked
    // overnight or a car over the weekend still counts in BookedHours, but not against the
    // office day.
    public static readonly TimeOnly WorkdayStart = new(9, 0);
    public static readonly TimeOnly WorkdayEnd = new(18, 0);

    public const string WorkingHoursLabel = "Mon-Fri 09:00-18:00";

    public const int TopUsers = 5;

    public static bool IsWorkday(DayOfWeek day) => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
