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
}

public sealed record UserFilter(
    AccountStatus? Status = null,
    string? Role = null,
    Guid? ManagerId = null,
    string? Search = null);

// MustChangePassword: the password was chosen by someone else - an Admin or Manager adding the
// account - so its owner has to replace it at their first sign-in (ADR-0021).
public sealed record NewAccount(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    AccountStatus Status,
    Guid? ManagerId,
    string Password,
    bool MustChangePassword = false);

public sealed record CreateAccountResult(Guid? Id, IReadOnlyList<string> PasswordErrors)
{
    public bool Succeeded => Id is not null;
}
