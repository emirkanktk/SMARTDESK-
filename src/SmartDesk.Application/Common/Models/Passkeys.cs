namespace SmartDesk.Application.Common.Models;

// A ceremony's options, as JSON the browser's WebAuthn API reads, and the token that goes back
// with the answer (ADR-0057).
public sealed record PasskeyChallenge(string OptionsJson, string Token);

// One of the user's passkeys. Id: its credential id, base64url. Synced: kept by a password manager
// that copies it to the owner's other devices, such as iCloud Keychain, rather than on one device.
public sealed record PasskeyDto(string Id, string Name, DateTimeOffset CreatedAt, bool Synced);

public enum PasskeyCheck
{
    Passed,

    // The token is past its time, used already, from another address, or not one SmartDesk made.
    Expired,

    // A passkey no account has: removed from the account, but still on the device.
    Unknown,

    // What the passkey sent doesn't hold up.
    Failed,
}

public sealed record PasskeyAdded(PasskeyCheck Check, PasskeyDto? Passkey);

public sealed record PasskeySignIn(PasskeyCheck Check, UserProfile? User);
