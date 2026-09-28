using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

// Idempotent: safe to run on every startup. Roles are reference data the app can't work
// without, so they're seeded in every environment; the demo users are Development-only
// (ADR-0007) and listed in the README.
public sealed class IdentityDataSeeder(
    SmartDeskDbContext dbContext,
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    ILogger<IdentityDataSeeder> logger)
{
    public const string DemoPassword = "SmartDesk123!";

    public static readonly IReadOnlyList<DemoUser> DemoUsers =
    [
        new("admin@smartdesk.local", "System", "Admin", Roles.Admin),
        new("manager@smartdesk.local", "Zeynep", "Kaya", Roles.Manager),
        new("employee1@smartdesk.local", "Ayşe", "Yılmaz", Roles.Employee, ManagerEmail: "manager@smartdesk.local"),
        new("employee2@smartdesk.local", "Mehmet", "Demir", Roles.Employee, ManagerEmail: "manager@smartdesk.local"),
    ];

    // Returns false when it skipped because the schema isn't up to date, so the caller can
    // skip any seeding that depends on it too.
    public async Task<bool> SeedAsync(bool includeDemoUsers, CancellationToken cancellationToken = default)
    {
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count != 0)
        {
            logger.LogWarning(
                "Skipping database seed: {Count} pending migration(s). Run 'dotnet ef database update' first.",
                pending.Count);
            return false;
        }

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole<Guid>(role)), $"create role '{role}'");
                logger.LogInformation("Seeded role {Role}", role);
            }
        }

        if (!includeDemoUsers)
        {
            return true;
        }

        foreach (var demo in DemoUsers)
        {
            var user = await userManager.FindByEmailAsync(demo.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = demo.Email,
                    Email = demo.Email,
                    EmailConfirmed = true,
                    FirstName = demo.FirstName,
                    LastName = demo.LastName,
                };
                EnsureSucceeded(await userManager.CreateAsync(user, DemoPassword), $"create user '{demo.Email}'");
                logger.LogInformation("Seeded demo user {Email}", demo.Email);
            }

            if (!await userManager.IsInRoleAsync(user, demo.Role))
            {
                EnsureSucceeded(await userManager.AddToRoleAsync(user, demo.Role), $"add '{demo.Email}' to '{demo.Role}'");
            }
        }

        // Second pass, once every demo user exists: put the employees in the manager's team
        // (also for databases seeded before teams existed).
        foreach (var demo in DemoUsers.Where(d => d.ManagerEmail is not null))
        {
            var member = await userManager.FindByEmailAsync(demo.Email);
            var manager = await userManager.FindByEmailAsync(demo.ManagerEmail!);
            if (member is not null && manager is not null && member.ManagerId is null)
            {
                member.ManagerId = manager.Id;
                EnsureSucceeded(await userManager.UpdateAsync(member), $"put '{demo.Email}' in '{demo.ManagerEmail}''s team");
            }
        }

        return true;
    }

    internal static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Identity seed failed to {operation}: {errors}");
        }
    }

    public sealed record DemoUser(string Email, string FirstName, string LastName, string Role, string? ManagerEmail = null);
}
