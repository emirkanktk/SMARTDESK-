using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Common.Models;

// Identity-agnostic view of a user, so Application code never touches ApplicationUser
// (which lives in Infrastructure - see docs/ARCHITECTURE.md §3.1).
public sealed record UserProfile(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<string> Roles)
{
    public AccountStatus Status { get; init; } = AccountStatus.Active;
    public Guid? ManagerId { get; init; }

    // A digest of the account's security stamp, for the token (ADR-0021).
    public string SessionStamp { get; init; } = string.Empty;

    // Set for an account whose password was chosen by someone else (ADR-0021).
    public bool MustChangePassword { get; init; }

    // The language emails to this user are written in: the one they last signed in with
    // (ADR-0023). Null until then; the request's language is used instead.
    public string? Language { get; init; }
}
