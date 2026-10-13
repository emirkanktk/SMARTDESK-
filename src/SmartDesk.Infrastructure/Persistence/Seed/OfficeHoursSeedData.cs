using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Persistence.Seed;

// The office's hours until an Admin sets them (ADR-0046): the working week the usage report
// has always measured against, in the zone SmartDesk was first set up for. Seeded through the
// migration (HasData), as the one row there always is.
public static class OfficeHoursSeedData
{
    public static OfficeHours Default => new()
    {
        Id = OfficeHours.SingletonId,
        TimeZone = "Europe/Istanbul",
        OpensAt = new TimeOnly(9, 0),
        ClosesAt = new TimeOnly(18, 0),
        Workdays = WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday,
    };
}
