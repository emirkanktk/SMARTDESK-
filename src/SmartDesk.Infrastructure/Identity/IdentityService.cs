using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IIdentityService
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
        if (result.IsLockedOut || result.IsNotAllowed)
        {
            // Identity answers these without looking at the password - same timing reason.
            VerifyDecoyPassword(password);
        }

        return result.Succeeded ? await ToProfileAsync(user) : null;
    }

    private void VerifyDecoyPassword(string password)
    {
        var hasher = userManager.PasswordHasher;
        s_decoyPasswordHash ??= hasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(new ApplicationUser(), s_decoyPasswordHash, password);
    }

    public async Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        return user is null ? null : await ToProfileAsync(user);
    }

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

        return new UserProfile(user.Id, user.FirstName, user.LastName, user.Email ?? string.Empty, roles.ToList());
    }
}
