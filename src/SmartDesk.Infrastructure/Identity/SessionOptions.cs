namespace SmartDesk.Infrastructure.Identity;

// How long a sign-in session lasts (ADR-0026).
public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    // Signed out after this long without the app renewing its token.
    public int IdleDays { get; init; } = 14;

    // Signed out after this long in any case, used or not.
    public int MaxDays { get; init; } = 30;

    // How long the refresh token a renewal replaced still works, for two tabs renewing at the
    // same moment. Used again after that, it can only be a copy, and the session ends.
    public int ReuseGraceSeconds { get; init; } = 30;
}
