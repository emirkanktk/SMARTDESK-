using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IIdentityService
{
    public async Task<UserProfile?> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        // Counts failed attempts and honours Identity's lockout (5 failures -> 5 minutes by
        // default), which a bare UserManager.CheckPasswordAsync would not.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        return result.Succeeded ? await ToProfileAsync(user) : null;
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
