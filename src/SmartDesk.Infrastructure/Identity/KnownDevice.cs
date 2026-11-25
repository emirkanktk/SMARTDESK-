namespace SmartDesk.Infrastructure.Identity;

// A browser an account has signed in on (ADR-0062), known by the token in that browser's cookie.
// The token is never stored, only its SHA-256 hash: one browser that several accounts signed in on
// has a row for each, with the same hash.
public sealed class KnownDevice
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }

    // Its latest sign-in for this account. Unused for IKnownDevices.RememberedFor, it's forgotten.
    public DateTime LastSeenAt { get; set; }
}
