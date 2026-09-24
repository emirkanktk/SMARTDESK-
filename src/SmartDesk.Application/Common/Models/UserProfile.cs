namespace SmartDesk.Application.Common.Models;

// Identity-agnostic view of a user, so Application code never touches ApplicationUser
// (which lives in Infrastructure - see docs/ARCHITECTURE.md §3.1).
public sealed record UserProfile(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<string> Roles);
