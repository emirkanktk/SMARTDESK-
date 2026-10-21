namespace SmartDesk.Infrastructure.Push;

// The server's identity at the push services (ADR-0056, VAPID - RFC 8292): a P-256 key pair,
// base64url, and how whoever runs the server can be reached. Without the keys notifications are off
// and nothing else notices. Make a pair with `dotnet run --project src/SmartDesk.API -- --vapid-keys`.
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    // Given to every browser that subscribes (applicationServerKey): the uncompressed point.
    public string PublicKey { get; init; } = string.Empty;

    // Never in appsettings - dotnet user-secrets locally, an environment variable elsewhere
    // (WebPush__PrivateKey), like the JWT key (ADR-0005).
    public string PrivateKey { get; init; } = string.Empty;

    // A contact for the push services: a mailto: address or an https: page. Apple refuses one on
    // localhost or a .local domain.
    public string Subject { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) || !string.IsNullOrWhiteSpace(PrivateKey);

    public bool HasValidSubject =>
        Uri.TryCreate(Subject, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == Uri.UriSchemeHttps)
        && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
