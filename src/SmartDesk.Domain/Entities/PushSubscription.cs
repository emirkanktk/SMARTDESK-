namespace SmartDesk.Domain.Entities;

// A phone or browser that turned on SmartDesk's notifications (ADR-0056): the address its push
// service takes them at, and the keys a notification is encrypted with for it, so the push service
// carries it without reading it. One row per device; the account is whoever turned them on there
// last.
public class PushSubscription
{
    public Guid Id { get; set; }

    // The account it notifies, with no foreign key across the Identity boundary, like
    // Reservation.UserId.
    public Guid UserId { get; set; }

    // The device's address at its push service: Apple's, Google's, Microsoft's or Mozilla's.
    public string Endpoint { get; set; } = string.Empty;

    // The device's P-256 public key and its authentication secret, base64url as the browser gives
    // them (RFC 8291).
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;

    // When notifications were last turned on there, or found on by its profile page: past the most
    // devices per person, the one seen longest ago goes.
    public DateTime SavedAt { get; set; }
}
