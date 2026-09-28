using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Identity;

// Makes sure the configured bootstrap admin exists and is an Admin, in any environment
// (ADR-0016). Idempotent, so it runs on every startup; the roles must already exist.
public sealed class BootstrapAdminSeeder(
    UserManager<ApplicationUser> userManager,
    IOptions<BootstrapAdminOptions> options,
    ILogger<BootstrapAdminSeeder> logger)
{
    public async Task SeedAsync()
    {
        var admin = options.Value;
        if (!admin.IsConfigured)
        {
            if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count == 0)
            {
                logger.LogWarning(
                    "No admin account exists. Set {Section}__Email and {Section}__Password to create one on startup.",
                    BootstrapAdminOptions.SectionName, BootstrapAdminOptions.SectionName);
            }

            return;
        }

        var user = await userManager.FindByEmailAsync(admin.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = admin.Email,
                Email = admin.Email,
                EmailConfirmed = true,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
            };
            IdentityDataSeeder.EnsureSucceeded(
                await userManager.CreateAsync(user, admin.Password), $"create the bootstrap admin '{admin.Email}'");
            logger.LogInformation("Created the bootstrap admin {Email}", admin.Email);
        }

        // An existing account keeps its password; it only gains the role.
        if (!await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            IdentityDataSeeder.EnsureSucceeded(
                await userManager.AddToRoleAsync(user, Roles.Admin), $"add '{admin.Email}' to '{Roles.Admin}'");
            logger.LogInformation("Added {Email} to the {Role} role", admin.Email, Roles.Admin);
        }

        // It's also the way back in if every admin got switched off (ADR-0020), so it's made
        // Active again, and taken out of any team since admins don't belong to one.
        if (user.Status != AccountStatus.Active || user.ManagerId is not null)
        {
            user.Status = AccountStatus.Active;
            user.ManagerId = null;
            IdentityDataSeeder.EnsureSucceeded(await userManager.UpdateAsync(user), $"reactivate '{admin.Email}'");
            logger.LogInformation("Reactivated the bootstrap admin {Email}", admin.Email);
        }
    }
}
