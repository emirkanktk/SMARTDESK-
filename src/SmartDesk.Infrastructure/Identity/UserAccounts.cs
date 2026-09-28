using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

public sealed class UserAccounts(
    UserManager<ApplicationUser> userManager,
    SmartDeskDbContext db,
    UserAccessCache accessCache) : IUserAccounts
{
    public async Task<UserAccount?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await ProjectAsync(db.Users.Where(u => u.Id == id), cancellationToken)).SingleOrDefault();

    public async Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = userManager.NormalizeEmail(email);
        return (await ProjectAsync(db.Users.Where(u => u.NormalizedEmail == normalized), cancellationToken)).SingleOrDefault();
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        await userManager.FindByEmailAsync(email) is not null;

    public async Task<IReadOnlyList<UserAccount>> ListAsync(UserFilter filter, CancellationToken cancellationToken)
    {
        var users = db.Users.AsQueryable();
        if (filter.Status is { } status)
        {
            users = users.Where(u => u.Status == status);
        }

        if (filter.ManagerId is { } managerId)
        {
            users = users.Where(u => u.ManagerId == managerId);
        }

        if (filter.Role is { } role)
        {
            users = users.Where(u => InRole(role).Contains(u.Id));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToUpperInvariant();
            users = users.Where(u =>
                u.NormalizedEmail!.Contains(term) || (u.FirstName + " " + u.LastName).ToUpper().Contains(term));
        }

        var accounts = await ProjectAsync(users, cancellationToken);

        // An account is listed under its highest role only (see ProjectAsync).
        return filter.Role is null ? accounts : accounts.Where(a => a.Role == filter.Role).ToList();
    }

    public Task<int> CountAsync(string role, AccountStatus status, CancellationToken cancellationToken) =>
        db.Users.CountAsync(u => u.Status == status && InRole(role).Contains(u.Id), cancellationToken);

    public async Task<CreateAccountResult> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = account.Email,
            Email = account.Email,
            FirstName = account.FirstName,
            LastName = account.LastName,
            Status = account.Status,
            ManagerId = account.ManagerId,
            CreatedAt = DateTime.UtcNow,
            MustChangePassword = account.MustChangePassword,
        };

        var result = await userManager.CreateAsync(user, account.Password);
        if (!result.Succeeded)
        {
            // Checked by the handlers first; this is two requests racing for the same email.
            if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                throw new ConflictException("A user with this email already exists.");
            }

            var passwordErrors = result.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).ToList();
            if (passwordErrors.Count == result.Errors.Count())
            {
                return new CreateAccountResult(null, passwordErrors.Select(e => e.Description).ToList());
            }

            throw new InvalidOperationException(
                "Could not create the account: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        IdentityDataSeeder.EnsureSucceeded(await userManager.AddToRoleAsync(user, account.Role), $"add '{account.Email}' to '{account.Role}'");
        return new CreateAccountResult(user.Id, []);
    }

    public async Task SetStatusAsync(Guid id, AccountStatus status, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(id);
        user.Status = status;
        IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"change the status of '{user.Email}'");
        Forget(id);
    }

    public async Task SetRoleAsync(Guid id, string role, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(id);
        var current = await userManager.GetRolesAsync(user);
        IdentityDataSeeder.EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, current), $"clear the roles of '{user.Email}'");
        IdentityDataSeeder.EnsureSucceeded(await userManager.AddToRoleAsync(user, role), $"make '{user.Email}' {role}");
        Forget(id);
    }

    public async Task SetManagerAsync(Guid id, Guid? managerId, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(id);
        user.ManagerId = managerId;
        IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"change the team of '{user.Email}'");

        // Their team decides which resources they can use (ADR-0022).
        Forget(id);
    }

    public async Task<IReadOnlyList<string>> SetTemporaryPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(id);

        // Through a reset token rather than remove-then-add: Identity checks the password first,
        // so a refused one leaves the old password in place, and the stamp moves on success.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            return result.Errors.Select(e => e.Description).ToList();
        }

        user.MustChangePassword = true;
        IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"require a password change for '{user.Email}'");
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        Forget(id);
        return [];
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(id);
        IdentityDataSeeder.EnsureSucceeded(await userManager.DeleteAsync(user), $"delete '{user.Email}'");
        Forget(id);
    }

    public async Task<IReadOnlyList<string>> CheckPasswordRulesAsync(string password)
    {
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, new ApplicationUser(), password);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }

        return errors;
    }

    public void HashDecoyPassword(string password) =>
        userManager.PasswordHasher.HashPassword(new ApplicationUser(), password);

    private async Task<ApplicationUser> RequireAsync(Guid id) =>
        await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("Account", id);

    private void Forget(Guid id) => accessCache.Forget(id);

    private IQueryable<Guid> InRole(string role) =>
        from userRole in db.UserRoles
        join r in db.Roles on userRole.RoleId equals r.Id
        where r.Name == role
        select userRole.UserId;

    private async Task<List<UserAccount>> ProjectAsync(IQueryable<ApplicationUser> users, CancellationToken cancellationToken)
    {
        var rows = await users
            .AsNoTracking()
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Status, u.ManagerId, u.CreatedAt, u.MustChangePassword })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();

        var roles = (await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken))
            .ToLookup(r => r.UserId, r => r.Name);

        var managerIds = rows.Where(r => r.ManagerId is not null).Select(r => r.ManagerId!.Value).Distinct().ToList();
        var managers = await db.Users
            .AsNoTracking()
            .Where(u => managerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var teamSizes = await db.Users
            .AsNoTracking()
            .Where(u => u.ManagerId != null && ids.Contains(u.ManagerId.Value))
            .GroupBy(u => u.ManagerId!.Value)
            .Select(g => new { ManagerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ManagerId, g => g.Count, cancellationToken);

        // Accounts have one role, but one could hold more (e.g. an existing Employee made Admin
        // by BootstrapAdmin). It counts as the highest: Admin, then Manager, then Employee.
        return rows.Select(r => new UserAccount(
                r.Id,
                r.FirstName,
                r.LastName,
                r.Email ?? string.Empty,
                Roles.All.FirstOrDefault(role => roles[r.Id].Contains(role)) ?? Roles.Employee,
                r.Status,
                r.ManagerId,
                r.ManagerId is { } m && managers.TryGetValue(m, out var manager)
                    ? $"{manager.FirstName} {manager.LastName}".Trim() is { Length: > 0 } name ? name : manager.Email
                    : null,
                r.CreatedAt,
                teamSizes.GetValueOrDefault(r.Id),
                r.MustChangePassword))
            .ToList();
    }
}
