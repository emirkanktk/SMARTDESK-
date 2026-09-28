namespace SmartDesk.Application.Features.Reservations.Common;

public static class ReservationRules
{
    // "No reservations in the past" (business rule 6) with a little slack, so a user who
    // picks the current quarter-hour at 14:03 to book the room they're standing in isn't
    // rejected by a clock that has already moved on.
    public static readonly TimeSpan PastStartGrace = TimeSpan.FromMinutes(5);

    // Same bound as the availability search window: a longer booking would block a resource
    // for over a month, which is far more likely a mistake than intent.
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(31);

    // Widest range the calendar query serves: a month grid shows six full weeks (the tail
    // of the previous month and the start of the next), i.e. 42 days - more than a month.
    public static readonly TimeSpan MaxCalendarRange = TimeSpan.FromDays(42);

    public const int MaxNoteLength = 500;

    // The reminder choices offered to the owner, in minutes before the start (ADR-0018).
    public static readonly IReadOnlyList<int> ReminderOptions = [5, 15, 30, 60];

    public static int MaxReminderMinutes => ReminderOptions[^1];

    public static bool IsValidReminder(int? minutes) => minutes is null || ReminderOptions.Contains(minutes.Value);

    public const string ReminderMessage = "Reminder must be 5, 15, 30 or 60 minutes before the start, or none.";
}
