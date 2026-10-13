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

    // It runs longer: its end moved later from the ending-soon notice (ADR-0033).
    MeetingExtended = 6,

    // A meeting that repeats (ADR-0035): one message for all its dates, not one each.
    // They were invited to every date of it.
    SeriesInvitation = 7,

    // Its organizer cancelled it from one date on.
    SeriesCancelled = 8,

    // To the organizer: someone invited is coming, from one date on.
    SeriesAccepted = 9,

    // To the organizer: someone invited can't come from one date on, and why.
    SeriesDeclined = 10,

    // To whoever reported a problem with a resource: it's fixed (ADR-0037). The times are
    // when it was reported and when it was resolved, the note is what they wrote.
    IssueResolved = 11,

    // To a booking's owner and everyone invited: it was cancelled because its resource was
    // blocked for that time, e.g. while a problem is fixed (ADR-0038). The reason is the block's.
    CancelledByBlock = 12,

    // To someone on a waiting list (ADR-0043): the time they waited for freed up, and it's
    // booked for them. The times are the booking's.
    WaitlistBooked = 13,
}
