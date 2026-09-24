namespace SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;

public sealed record CurrentUserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<string> Roles);
