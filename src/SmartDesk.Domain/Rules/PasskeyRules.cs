namespace SmartDesk.Domain.Rules;

// Passkeys: signing in with what unlocks the device - Face ID, a fingerprint, Windows Hello -
// instead of a password (ADR-0057).
public static class PasskeyRules
{
    // Passkeys one account keeps: a phone, a laptop, a security key, and room to spare.
    public const int MaxPerUser = 10;

    // What the owner calls one in their list, such as "iPhone".
    public const int MaxNameLength = 50;

    // A browser's answer is a few hundred bytes, since SmartDesk asks for no attestation; one far
    // longer isn't worth reading.
    public const int MaxCredentialLength = 16 * 1024;

    // How long the browser has to answer a challenge: a person looking at their phone, or finding
    // their security key.
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
}
