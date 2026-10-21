namespace SmartDesk.Domain.Rules;

// Notifications on phones and browsers (ADR-0056).
public static class PushRules
{
    // Devices one account gets notifications on; turning them on on one more forgets the oldest.
    public const int MaxDevicesPerUser = 10;

    public const int MaxEndpointLength = 2048;

    // A device's public key - an uncompressed P-256 point - and its authentication secret, as
    // bytes (RFC 8291).
    public const int PublicKeyLength = 65;
    public const int AuthSecretLength = 16;

    // The push services browsers use. The server posts to whatever address a device gives it, so
    // only theirs are taken: anything else - this machine, the office network - is refused rather
    // than requested on someone's say-so.
    public static IReadOnlyList<string> KnownServices { get; } =
    [
        // Safari, and apps added to an iPhone's home screen.
        "push.apple.com",
        // Chrome and the browsers built on it.
        "fcm.googleapis.com",
        // Edge on Windows.
        "notify.windows.com",
        // Firefox.
        "push.services.mozilla.com",
    ];

    public static bool IsKnownService(Uri endpoint) =>
        endpoint.IsAbsoluteUri
        && endpoint.Scheme == Uri.UriSchemeHttps
        && endpoint.IsDefaultPort
        && KnownServices.Any(service =>
            endpoint.Host.Equals(service, StringComparison.OrdinalIgnoreCase)
            || endpoint.Host.EndsWith("." + service, StringComparison.OrdinalIgnoreCase));
}
