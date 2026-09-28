using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Common.Interfaces;

// Reading and changing accounts, for registration and the user-management features (ADR-0020).
// The rules - who may do what to whom - live in the handlers; this only stores and fetches.
// Every change takes effect on the account's next request, since it invalidates what
// IUserAccessChecker remembers about that user.
public interface IUserAccounts
{
    Task<UserAccount?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserAccount>> ListAsync(UserFilter filter, CancellationToken cancellationToken);

    Task<int> CountAsync(string role, AccountStatus status, CancellationToken cancellationToken);

    // Returns Identity's reasons when it refuses the password.
    Task<CreateAccountResult> CreateAsync(NewAccount account, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid id, AccountStatus status, CancellationToken cancellationToken);

    Task SetRoleAsync(Guid id, string role, CancellationToken cancellationToken);

    Task SetManagerAsync(Guid id, Guid? managerId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    // Identity's reasons for refusing this password, or none. Registration asks before looking
    // the email up, so a weak password gets the same answer whether or not the email is taken.
    Task<IReadOnlyList<string>> CheckPasswordRulesAsync(string password);

    // As much work as hashing a password, for a path that must take as long as creating an
    // account (registration with an email that already has one).
    void HashDecoyPassword(string password);
}

// Asked on every authenticated request: is this token's user still Active, with the roles the
// token claims? Answers are cached briefly; IUserAccounts clears them when an account changes.
public interface IUserAccessChecker
{
    Task<bool> IsCurrentAsync(Guid userId, IReadOnlyCollection<string> tokenRoles, CancellationToken cancellationToken);
}
