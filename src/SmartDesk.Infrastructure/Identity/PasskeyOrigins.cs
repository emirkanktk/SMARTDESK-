using Microsoft.Extensions.Configuration;

namespace SmartDesk.Infrastructure.Identity;

// Where the app is served (ADR-0057): App:FrontendUrl, and the addresses Cors:AllowedOrigins lets
// call the API. A passkey belongs to the domain of the page it was made on, so these are the only
// pages that may make or use SmartDesk's.
public sealed class PasskeyOrigins
{
    private readonly HashSet<string> _origins;

    public PasskeyOrigins(IEnumerable<string?> urls) =>
        _origins = urls.Select(Normalize).OfType<string>().ToHashSet(StringComparer.Ordinal);

    public static PasskeyOrigins From(IConfiguration configuration) =>
        new([
            configuration[$"{AppOptions.SectionName}:{nameof(AppOptions.FrontendUrl)}"],
            .. configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [],
        ]);

    // The origin - scheme, host and port, as a browser's Origin header has it - when the app is
    // served there; null for anywhere else.
    public string? Find(string? origin) => Normalize(origin) is { } known && _origins.Contains(known) ? known : null;

    private static string? Normalize(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;
}
