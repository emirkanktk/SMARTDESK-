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
    int TeamSize)
{
    public string DisplayName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : Email;
}

public sealed record UserFilter(
    AccountStatus? Status = null,
    string? Role = null,
    Guid? ManagerId = null,
    string? Search = null);

// Password null: the account gets none, and its owner sets one through an emailed link.
public sealed record NewAccount(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    AccountStatus Status,
    Guid? ManagerId,
    string? Password);

public sealed record CreateAccountResult(Guid? Id, IReadOnlyList<string> PasswordErrors)
{
    public bool Succeeded => Id is not null;
}
