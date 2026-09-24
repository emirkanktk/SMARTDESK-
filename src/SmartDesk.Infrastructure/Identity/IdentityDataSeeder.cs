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
        new("employee1@smartdesk.local", "Ayşe", "Yılmaz", Roles.Employee),
        new("employee2@smartdesk.local", "Mehmet", "Demir", Roles.Employee),
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

        foreach (var role in new[] { Roles.Admin, Roles.Employee })
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

        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Identity seed failed to {operation}: {errors}");
        }
    }

    public sealed record DemoUser(string Email, string FirstName, string LastName, string Role);
}
