namespace SmartDesk.Domain.Constants;

public static class Roles
{
    public const string Admin = "Admin";

    // Runs a team: adds, switches off and removes their own members (ADR-0020).
    public const string Manager = "Manager";

    public const string Employee = "Employee";

    // Every account has exactly one of these; the most powerful one wins if a user somehow has
    // several (Admin > Manager > Employee).
    public static readonly IReadOnlyList<string> All = [Admin, Manager, Employee];
}
