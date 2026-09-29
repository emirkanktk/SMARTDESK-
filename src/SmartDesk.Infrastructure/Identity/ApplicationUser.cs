using Microsoft.AspNetCore.Identity;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public AccountStatus Status { get; set; } = AccountStatus.Active;

    // The Manager whose team this user is in, if any (ADR-0020). Managers and Admins have none.
    public Guid? ManagerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The password was chosen by someone else (an Admin or Manager who added the account or
    // set a temporary one); the owner must replace it before doing anything else (ADR-0021).
    public bool MustChangePassword { get; set; }

    // "en" or "tr": the language the user last signed in with, which their emails are written
    // in (ADR-0023). Null for an account that hasn't signed in since this was added.
    public string? Language { get; set; }
}
