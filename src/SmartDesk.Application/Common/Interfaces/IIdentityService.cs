using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

public interface IIdentityService
{
    // Returns null for an unknown email, a wrong password, or a locked-out account - callers
    // must not be able to tell those apart, so an attacker can't probe for valid emails.
    Task<UserProfile?> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    // "First Last" (or the email when both are blank) for each id that exists, in one query.
    // Unknown ids are simply missing from the result.
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    // A reset token for the account with this email, or null when there is none. Callers must
    // treat both outcomes the same way towards the client (ADR-0019).
    Task<PasswordResetToken?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken);

    // The user's own password change (ADR-0021): checks the current password, sets the new
    // one under Identity's policy, and clears "must change password". Ends every other
    // session, since the security stamp moves.
    Task<ChangePasswordResult> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    // Sets a new password if the token is valid for this email. A successful reset also ends a
    // lockout, so someone locked out by failed attempts can get back in.
    Task<ResetPasswordResult> ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken);
}
