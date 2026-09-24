using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

public interface IIdentityService
{
    // Returns null for an unknown email, a wrong password, or a locked-out account - callers
    // must not be able to tell those apart, so an attacker can't probe for valid emails.
    Task<UserProfile?> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
