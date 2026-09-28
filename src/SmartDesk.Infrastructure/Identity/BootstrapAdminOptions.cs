namespace SmartDesk.Infrastructure.Identity;

// The first admin of a deployed environment, where the Development demo users don't exist
// (ADR-0016). Set it from environment variables or a secret store, never in appsettings:
// BootstrapAdmin__Email and BootstrapAdmin__Password.
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string Email { get; init; } = string.Empty;

    // Only used when the account is created; an existing account's password is never changed.
    public string Password { get; init; } = string.Empty;

    public string FirstName { get; init; } = "System";
    public string LastName { get; init; } = "Admin";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    // Either both or neither: one without the other is a configuration mistake, and failing
    // at startup beats silently starting without an admin.
    public bool IsComplete => string.IsNullOrWhiteSpace(Email) == string.IsNullOrWhiteSpace(Password);
}
