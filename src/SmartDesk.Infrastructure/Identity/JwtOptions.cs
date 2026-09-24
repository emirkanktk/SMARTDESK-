namespace SmartDesk.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // HMAC-SHA256 needs a key of at least 256 bits; shorter keys fail at token creation time.
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    // Never in appsettings - dotnet user-secrets locally, environment variable elsewhere (ADR-0005).
    public string Key { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 60;
}
