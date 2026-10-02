namespace SmartDesk.Domain.Enums;

// What someone invited to a meeting answered (ADR-0032).
public enum AttendeeResponse
{
    // Not yet, or not since the meeting moved to a new start or another room.
    Pending = 0,

    Accepted = 1,

    // They can't come, and said why.
    Declined = 2,
}
