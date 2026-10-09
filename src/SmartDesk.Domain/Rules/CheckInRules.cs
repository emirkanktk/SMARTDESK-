using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Rules;

// Checking in at the door with the resource's QR code (ADR-0030). A booking that needs it can
// be checked in from shortly before its start until its grace after, a quarter of an hour by
// default. One nobody has checked in to by then is released, so the room goes back to people
// who are actually there. The grace is a setting (ADR-0040), so the rules take it.
public static class CheckInRules
{
    public static readonly TimeSpan OpensBefore = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(15);

    // The shortest and longest grace the setting takes: a demo can show a release in a minute,
    // and an hour-long grace would keep most rooms waiting for no one.
    public static readonly TimeSpan MinGrace = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxGrace = TimeSpan.FromMinutes(60);

    // How long before the release the owner and those invited are warned (ADR-0034).
    public static readonly TimeSpan WarnBeforeRelease = TimeSpan.FromMinutes(5);

    // The warning's lead for a grace: WarnBeforeRelease, or the whole grace when it's shorter,
    // so the warning never comes before the start.
    public static TimeSpan WarningLead(TimeSpan grace) => grace < WarnBeforeRelease ? grace : WarnBeforeRelease;

    // Its resource asked for a check-in when it was booked, and still does. So switching
    // check-in on doesn't catch bookings made without knowing, and switching it off lets every
    // booking be at once.
    public static Expression<Func<Reservation, bool>> Needed =>
        r => r.CheckInRequired && r.Resource.CheckInRequired;

    // Its window is open at `now`: [start - OpensBefore, start + grace), and never once the
    // booking is over.
    public static Expression<Func<Reservation, bool>> OpenAt(DateTime now, TimeSpan grace)
    {
        var startsBy = now + OpensBefore;
        var startedAfter = now - grace;
        return r => r.StartDateTime <= startsBy && startedAfter < r.StartDateTime && now < r.EndDateTime;
    }

    // Released at `now`: Needed, Confirmed, nobody checked in, and the window has closed while
    // the booking still has time left for someone else. With the same grace, its start condition
    // is exactly the opposite of OpenAt's, so a check-in and a release are never both allowed.
    public static Expression<Func<Reservation, bool>> DueForRelease(DateTime now, TimeSpan grace)
    {
        var closedFor = now - grace;
        return r => r.Status == ReservationStatus.Confirmed
            && r.CheckInRequired
            && r.Resource.CheckInRequired
            && r.CheckedInAt == null
            && r.StartDateTime <= closedFor
            && now < r.EndDateTime;
    }

    // Warned at `now`: Needed, Confirmed, nobody checked in, not warned yet, and the window
    // closes within its WarningLead - but hasn't closed: from then on, it's the release.
    public static Expression<Func<Reservation, bool>> DueForReleaseWarning(DateTime now, TimeSpan grace)
    {
        var warnFrom = now - (grace - WarningLead(grace));
        var closedFor = now - grace;
        return r => r.Status == ReservationStatus.Confirmed
            && r.CheckInRequired
            && r.Resource.CheckInRequired
            && r.CheckedInAt == null
            && r.ReleaseWarningSentAt == null
            && r.StartDateTime <= warnFrom
            && closedFor < r.StartDateTime
            && now < r.EndDateTime;
    }

    // The secret in a resource's QR code: 16 random bytes as 32 hex digits.
    public const int CodeLength = 32;

    public static string NewCode() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CodeLength / 2));

    // In constant time, so how long a wrong guess takes says nothing about how close it was.
    public static bool CodeMatches(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));

    // --- A door screen's changing code (ADR-0034) ---

    // A new code every 30 seconds; the one before is still taken, so a code scanned as it
    // changes still works. A photo of the screen is good for a minute at most.
    public static readonly TimeSpan CodeStep = TimeSpan.FromSeconds(30);

    // 10 bytes of an HMAC-SHA256 as hex: 80 bits, far beyond guessing within a minute.
    public const int ChangingCodeLength = 20;

    public static long StepAt(DateTime utcNow) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() / (long)CodeStep.TotalSeconds;

    // When the step at `utcNow` ends, and the screen shows the next code.
    public static DateTime StepEndsAt(DateTime utcNow) =>
        DateTimeOffset.FromUnixTimeSeconds((StepAt(utcNow) + 1) * (long)CodeStep.TotalSeconds).UtcDateTime;

    // The code for a step, keyed by the screen's own key (ADR-0039): its token's hash, which no
    // endpoint ever returns. Not the printed code's secret: a photo of the printed code would
    // then compute every screen code. A new screen link changes them too.
    public static string ChangingCode(string screenKey, long step)
    {
        var message = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, step);
        var mac = HMACSHA256.HashData(Convert.FromHexString(screenKey), message);
        return Convert.ToHexStringLower(mac.AsSpan(0, ChangingCodeLength / 2));
    }

    public static string ChangingCodeAt(string screenKey, DateTime utcNow) => ChangingCode(screenKey, StepAt(utcNow));

    // This step's code or the last one's, each compared in constant time.
    public static bool ChangingCodeMatches(string screenKey, string? presented, DateTime utcNow)
    {
        var step = StepAt(utcNow);
        var current = CodeMatches(ChangingCode(screenKey, step), presented);
        var previous = CodeMatches(ChangingCode(screenKey, step - 1), presented);
        return current | previous;
    }

    // --- A door screen's own token ---

    // 32 random bytes as 64 hex digits; given out once, kept only as its hash.
    public static string NewDoorScreenToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string HashDoorScreenToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool DoorScreenTokenMatches(string? storedHash, string? presented) =>
        storedHash is not null
        && presented is not null
        && CodeMatches(storedHash, HashDoorScreenToken(presented));
}
