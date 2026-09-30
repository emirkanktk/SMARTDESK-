namespace SmartDesk.Infrastructure.Identity;

// One sign-in session (ADR-0026). Refresh tokens are never stored, only their SHA-256 hashes: the
// current one, and the one it replaced, which still works for a few seconds so two renewals
// racing each other don't end the session.
public sealed class UserSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string? PreviousTokenHash { get; set; }

    public DateTime? RotatedAt { get; set; }

    // UserAccessChecker.SessionStampOf(the security stamp) when the session started: a password
    // change moves the stamp, so the session can't be renewed any more.
    public string SessionStamp { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Moves forward with every renewal, up to AbsoluteExpiresAt.
    public DateTime ExpiresAt { get; set; }

    public DateTime AbsoluteExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
