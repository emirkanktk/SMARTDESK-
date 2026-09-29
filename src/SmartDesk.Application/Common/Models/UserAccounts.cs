using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Common.Models;

// One account as the user-management screens see it (ADR-0020).
public sealed record UserAccount(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    AccountStatus Status,
    Guid? ManagerId,
    string? ManagerName,
    DateTime CreatedAt,
    int TeamSize,
    bool MustChangePassword = false)
{
    public string DisplayName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : Email;

    // The language emails to them are written in (ADR-0023); null until they first sign in.
    public string? Language { get; init; }
}

public sealed record UserFilter(
    AccountStatus? Status = null,
    string? Role = null,
    Guid? ManagerId = null,
    string? Search = null);

// MustChangePassword: the password was chosen by someone else - an Admin or Manager adding the
// account - so its owner has to replace it at their first sign-in (ADR-0021). Language: what
// their emails are written in until they sign in themselves (ADR-0023).
public sealed record NewAccount(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    AccountStatus Status,
    Guid? ManagerId,
    string Password,
    bool MustChangePassword = false,
    string? Language = null);

public sealed record CreateAccountResult(Guid? Id, IReadOnlyList<string> PasswordErrors)
{
    public bool Succeeded => Id is not null;
}
