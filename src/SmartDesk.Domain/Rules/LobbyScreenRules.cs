namespace SmartDesk.Domain.Rules;

// Lobby screens (ADR-0064).
public static class LobbyScreenRules
{
    public const int MaxNameLength = 60;

    // An office has a handful of such screens; more is a script.
    public const int MaxScreens = 20;

    // A screen asks twice a minute: noting each would write for nothing.
    public static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);

    // How many of a room's bookings still to come a screen shows: the next few hours' worth.
    public const int MaxBookingsPerRoom = 6;

    // Its token is a door screen's kind: 32 random bytes as 64 hex digits, given out once and kept
    // only as its SHA-256.
    public static string NewToken() => CheckInRules.NewDoorScreenToken();

    public static string HashToken(string token) => CheckInRules.HashDoorScreenToken(token);

    public static bool IsWellFormed(string? token) =>
        token is { Length: 64 } && token.All(c => char.IsAsciiHexDigitLower(c));
}
