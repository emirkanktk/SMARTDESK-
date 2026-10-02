namespace SmartDesk.Application.Features.Meetings.Common;

// Meetings (ADR-0032): a booking with people invited to it.
public static class MeetingRules
{
    // A meeting room for a whole team; more is far more likely a mistake.
    public const int MaxAttendees = 50;

    // Why someone can't come, as long as a booking's note.
    public const int ReasonMaxLength = 500;

    // An inbox shows the latest messages; older ones stay in the database.
    public const int InboxPageSize = 100;
}
