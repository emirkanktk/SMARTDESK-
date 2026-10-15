namespace SmartDesk.Domain.Entities;

// Someone's personal calendar link (ADR-0048): a calendar app subscribed to it keeps their
// bookings and the meetings they're invited to in step. The link carries a secret token, kept
// only as its hash, so the link can't be shown again: a new one replaces it. One per account.
public class CalendarFeed
{
    // The account it's for, with no foreign key across the Identity boundary, like
    // Reservation.UserId.
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // When a calendar app last fetched it, so its owner can see the link is in use; null until
    // one has.
    public DateTime? LastFetchedAt { get; set; }
}
