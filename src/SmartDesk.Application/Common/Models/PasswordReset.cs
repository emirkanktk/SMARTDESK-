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
