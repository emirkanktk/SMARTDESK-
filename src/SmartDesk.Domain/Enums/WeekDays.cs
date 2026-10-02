namespace SmartDesk.Domain.Enums;

// The days of the week a weekly series is on (ADR-0035), stored as one number.
[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}
