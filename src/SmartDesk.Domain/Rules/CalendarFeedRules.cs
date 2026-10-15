using System.Security.Cryptography;
using System.Text;

namespace SmartDesk.Domain.Rules;

// A personal calendar link (ADR-0048): its token, and how much of the calendar it carries.
public static class CalendarFeedRules
{
    // What's past stays this long: a calendar app replaces its copy with each fetch, so last
    // month's bookings are still there to look back on.
    public const int DaysBack = 90;

    // How often a calendar app is asked to fetch it again. A hint: Google Calendar keeps to its
    // own pace of several hours.
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    // 32 random bytes as 64 hex digits, like a door screen's token: given out once, kept only
    // as its hash.
    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Anything else can't be a token, so it's turned away before the database is asked.
    public static bool LooksLikeToken(string? token) =>
        token is { Length: 64 } && token.All(char.IsAsciiHexDigitLower);
}
