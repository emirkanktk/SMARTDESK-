namespace SmartDesk.Domain.Enums;

// What an inbox message tells its reader about a meeting they're invited to, or one they
// organized (ADR-0032).
public enum InboxMessageKind
{
    // They were invited.
    MeetingInvitation = 0,

    // Its time or room changed.
    MeetingChanged = 1,

    // It was cancelled, or released because nobody checked in.
    MeetingCancelled = 2,

    // They were taken off the list.
    MeetingUninvited = 3,

    // To the organizer: someone invited is coming.
    MeetingAccepted = 4,

    // To the organizer: someone invited can't come, and why.
    MeetingDeclined = 5,
}
