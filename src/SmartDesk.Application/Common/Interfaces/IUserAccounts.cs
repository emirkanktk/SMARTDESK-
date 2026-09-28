using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Common.Interfaces;

// Reading and changing accounts, for registration and the user-management features (ADR-0020).
// The rules - who may do what to whom - live in the handlers; this only stores and fetches.
// Every change takes effect on the account's next request, since it invalidates what
// IUserAccessChecker remembers about that user and ends their real-time connections.
public interface IUserAccounts
{
    Task<UserAccount?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserAccount>> ListAsync(UserFilter filter, CancellationToken cancellationToken);

    Task<int> CountAsync(string role, AccountStatus status, CancellationToken cancellationToken);

    // Returns Identity's reasons when it refuses the password.
    Task<CreateAccountResult> CreateAsync(NewAccount account, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid id, AccountStatus status, CancellationToken cancellationToken);

    Task SetRoleAsync(Guid id, string role, CancellationToken cancellationToken);

    Task SetManagerAsync(Guid id, Guid? managerId, CancellationToken cancellationToken);

    // Replaces the password with one chosen by someone else, which the owner must change on
    // their next sign-in (ADR-0021). Ends the account's sessions and any lockout. Returns
    // Identity's reasons when it refuses the password; nothing changes then.
    Task<IReadOnlyList<string>> SetTemporaryPasswordAsync(Guid id, string password, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    // Identity's reasons for refusing this password, or none. Registration asks before looking
    // the email up, so a weak password gets the same answer whether or not the email is taken.
    Task<IReadOnlyList<string>> CheckPasswordRulesAsync(string password);

    // As much work as hashing a password, for a path that must take as long as creating an
    // account (registration with an email that already has one).
    void HashDecoyPassword(string password);
}

// What a request needs to know about its user as they are now, not as their token says.
// Cached briefly; IUserAccounts clears it whenever it changes the account.
public sealed record UserAccessSnapshot(
    AccountStatus Status,
    IReadOnlySet<string> Roles,
    string SessionStamp,
    Guid? ManagerId,
    bool MustChangePassword);

public interface IUserAccessChecker
{
    // Null when the user no longer exists.
    Task<UserAccessSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken);
}

// The user's open real-time (SignalR) connections. Ending them makes the client reconnect,
// which re-checks the account, so a disabled user stops receiving events at once.
public interface IRealtimeSessions
{
    void End(Guid userId);
}
