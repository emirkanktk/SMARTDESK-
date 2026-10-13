namespace SmartDesk.Domain.Enums;

// Days of the week, stored as one number: those a weekly series is on (ADR-0035), and the
// office's workdays (ADR-0046).
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
