using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Rules;

// Checking in at the door with the resource's QR code (ADR-0030). A booking that needs it can
// be checked in from shortly before its start until a quarter of an hour after. One nobody has
// checked in to by then is released, so the room goes back to people who are actually there.
public static class CheckInRules
{
    public static readonly TimeSpan OpensBefore = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    // Its resource asked for a check-in when it was booked, and still does. So switching
    // check-in on doesn't catch bookings made without knowing, and switching it off lets every
    // booking be at once.
    public static Expression<Func<Reservation, bool>> Needed =>
        r => r.CheckInRequired && r.Resource.CheckInRequired;

    // Its window is open at `now`: [start - OpensBefore, start + Grace), and never once the
    // booking is over.
    public static Expression<Func<Reservation, bool>> OpenAt(DateTime now)
    {
        var startsBy = now + OpensBefore;
        var startedAfter = now - Grace;
        return r => r.StartDateTime <= startsBy && startedAfter < r.StartDateTime && now < r.EndDateTime;
    }

    // Released at `now`: Needed, Confirmed, nobody checked in, and the window has closed while
    // the booking still has time left for someone else. Its start condition is exactly the
    // opposite of OpenAt's, so a check-in and a release are never both allowed.
    public static Expression<Func<Reservation, bool>> DueForRelease(DateTime now)
    {
        var closedFor = now - Grace;
        return r => r.Status == ReservationStatus.Confirmed
            && r.CheckInRequired
            && r.Resource.CheckInRequired
            && r.CheckedInAt == null
            && r.StartDateTime <= closedFor
            && now < r.EndDateTime;
    }

    // The secret in a resource's QR code: 16 random bytes as 32 hex digits.
    public const int CodeLength = 32;

    public static string NewCode() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CodeLength / 2));

    // In constant time, so how long a wrong guess takes says nothing about how close it was.
    public static bool CodeMatches(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
}
