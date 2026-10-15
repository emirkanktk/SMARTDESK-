namespace SmartDesk.Application.Features.Calendar.Common;

// A calendar to write as an .ics file (ADR-0048): one booking to add to a calendar app, or a
// person's calendar feed. Times are UTC, which every calendar app shows in its own time zone;
// the words are already in the reader's language.
public sealed record CalendarDocument(string Name, DateTime WrittenAt, IReadOnlyList<CalendarEntry> Entries)
{
    // How often a calendar app subscribed to it should fetch it again; null for a file that's
    // added once.
    public TimeSpan? RefreshInterval { get; init; }
}

// One booking as a calendar event. Uid stays the same for the booking, so a calendar app updates
// its copy when the booking moves rather than adding another.
public sealed record CalendarEntry(
    string Uid,
    DateTime Start,
    DateTime End,
    string Summary,
    string Location,
    string Description,
    string Url,
    CalendarEntryStatus Status);

public enum CalendarEntryStatus
{
    Confirmed,

    // A meeting the reader hasn't answered yet.
    Tentative,

    Cancelled,
}

// Writes a calendar in the iCalendar format (RFC 5545). Implemented in Infrastructure.
public interface ICalendarWriter
{
    byte[] Write(CalendarDocument calendar);
}
