namespace SmartDesk.Application.Common.Models;

// A reset token for an existing account. The token is URL-safe and opaque to Application.
public sealed record PasswordResetToken(UserProfile User, string Token);

public sealed record ResetPasswordResult(bool Succeeded, bool TokenInvalid, IReadOnlyList<string> PasswordErrors)
{
    public static readonly ResetPasswordResult Success = new(true, false, []);

    // Wrong, expired or already used - and also an unknown email, so a reset link can't be
    // used to probe which emails have accounts.
    public static readonly ResetPasswordResult InvalidToken = new(false, true, []);

    public static ResetPasswordResult RejectedPassword(IReadOnlyList<string> errors) => new(false, false, errors);
}

// The outcome of a user changing their own password (ADR-0021). On success, the profile
// as it is now - with the new session stamp - for issuing a fresh token.
public sealed record ChangePasswordResult(UserProfile? User, bool WrongCurrentPassword, IReadOnlyList<string> PasswordErrors)
{
    public bool Succeeded => User is not null;

    public static ChangePasswordResult Success(UserProfile user) => new(user, false, []);

    public static readonly ChangePasswordResult WrongCurrent = new(null, true, []);

    public static ChangePasswordResult RejectedPassword(IReadOnlyList<string> errors) => new(null, false, errors);
}
