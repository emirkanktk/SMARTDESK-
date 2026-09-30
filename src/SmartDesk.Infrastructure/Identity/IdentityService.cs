using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    UserAccessCache accessCache) : IIdentityService
{
    // A hash of a random password, made once with the app's own hasher so checking against it
    // costs the same as checking a real user's password.
    private static string? s_decoyPasswordHash;

    public async Task<UserProfile?> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Checking a password is deliberately slow (PBKDF2). Skipping it for an unknown
            // email made "no such account" answer several times faster than "wrong password"
            // (about 12 ms against 75 ms locally), so response times alone told an attacker
            // which emails have accounts.
            VerifyDecoyPassword(password);
            return null;
        }

        // Counts failed attempts and honours Identity's lockout (5 failures -> 5 minutes by
        // default), which a bare UserManager.CheckPasswordAsync would not.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut || result.IsNotAllowed || user.PasswordHash is null)
        {
            // Identity answers these without hashing the password - same timing reason. An
            // account without a password can only come from before ADR-0021, when invited
            // accounts got theirs from an emailed link.
            VerifyDecoyPassword(password);
        }

        return result.Succeeded ? await ToProfileAsync(user) : null;
    }

    public async Task<PasswordResetToken?> CreatePasswordResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        // Identity's tokens are base64 with '+', '/' and '='; base64url keeps them intact in a link.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new PasswordResetToken(await ToProfileAsync(user), WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken)
    {
        string identityToken;
        try
        {
            identityToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return ResetPasswordResult.InvalidToken;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return ResetPasswordResult.InvalidToken;
        }

        // Checks the token before the password, and changes the security stamp on success, so
        // the same link can't be used twice.
        var result = await userManager.ResetPasswordAsync(user, identityToken, newPassword);
        if (result.Succeeded)
        {
            await userManager.ResetAccessFailedCountAsync(user);
            await userManager.SetLockoutEndDateAsync(user, null);

            // The owner chose this one themselves, so a temporary password is replaced too.
            if (user.MustChangePassword)
            {
                user.MustChangePassword = false;
                IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"clear the password change for '{user.Email}'");
            }

            // The stamp moved: every earlier token is refused from the next request.
            accessCache.Forget(user.Id);
            return ResetPasswordResult.Success;
        }

        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
            ? ResetPasswordResult.InvalidToken
            : ResetPasswordResult.RejectedPassword(result.Errors.Select(e => e.Description).ToList());
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new UnauthorizedException();

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? ChangePasswordResult.WrongCurrent
                : ChangePasswordResult.RejectedPassword(result.Errors.Select(e => e.Description).ToList());
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"clear the password change for '{user.Email}'");
        }

        // ChangePasswordAsync moved the security stamp, which ends every other session.
        accessCache.Forget(user.Id);
        return ChangePasswordResult.Success(await ToProfileAsync(user));
    }

    private void VerifyDecoyPassword(string password)
    {
        var hasher = userManager.PasswordHasher;
        s_decoyPasswordHash ??= hasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(new ApplicationUser(), s_decoyPasswordHash, password);
    }

    // As the database has it now, not as this request may already have loaded it: a change
    // written straight to the columns (RenameAsync, RememberLanguageAsync) shows at once.
    public async Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null ? null : await ToProfileAsync(user);
    }

    // Written straight to the column: going through UserManager.UpdateAsync would also
    // re-save everything else on the account, racing a concurrent change made by an Admin.
    public async Task RememberLanguageAsync(Guid userId, string language, CancellationToken cancellationToken) =>
        await userManager.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.Language, language), cancellationToken);

    // Straight to the columns too, for the same reason.
    public async Task RenameAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken) =>
        await userManager.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(u => u.FirstName, firstName).SetProperty(u => u.LastName, lastName),
                cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name ? name : u.Email ?? string.Empty);
    }

    private async Task<UserProfile> ToProfileAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);

        return new UserProfile(user.Id, user.FirstName, user.LastName, user.Email ?? string.Empty, roles.ToList())
        {
            Status = user.Status,
            ManagerId = user.ManagerId,
            SessionStamp = UserAccessChecker.SessionStampOf(user.SecurityStamp),
            MustChangePassword = user.MustChangePassword,
            Language = user.Language,
        };
    }
}
