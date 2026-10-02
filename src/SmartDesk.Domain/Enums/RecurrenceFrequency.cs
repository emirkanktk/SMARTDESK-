namespace SmartDesk.Domain.Enums;

// How often a repeating booking comes back (ADR-0035): every so many days, or on chosen days of
// every so many weeks.
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
}
