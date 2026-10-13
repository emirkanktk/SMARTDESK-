using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// When the office is open (ADR-0046): one row, which an Admin sets. The kinds of resource that
// are booked within office hours only keep to them, and the usage report measures against them.
public class OfficeHours
{
    // There's one office, so one row.
    public const int SingletonId = 1;

    public int Id { get; set; }

    // The IANA zone the hours are in: "Europe/Istanbul".
    public string TimeZone { get; set; } = string.Empty;

    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public WeekDays Workdays { get; set; }
}
