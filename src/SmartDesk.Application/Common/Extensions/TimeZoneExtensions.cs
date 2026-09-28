namespace SmartDesk.Application.Common.Extensions;

public static class TimeZoneExtensions
{
    // The UTC instants where the local calendar day containing `utcNow` starts and ends, in
    // the given zone. "Today" has to be the user's day, not the server's: with the server on
    // UTC, 01:30 in Istanbul (UTC+3) would otherwise still be "yesterday". Days aren't always
    // 24 hours - across a daylight-saving change they are 23 or 25 - so both midnights are
    // converted separately instead of adding 24 hours.
    public static (DateTime StartUtc, DateTime EndUtc) LocalDayBoundsUtc(this TimeZoneInfo zone, DateTime utcNow)
    {
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone).Date;

        return (zone.LocalToUtc(localDate), zone.LocalToUtc(localDate.AddDays(1)));
    }

    // The UTC instant of a local wall-clock time in the zone. A time the clocks skip (02:30 on
    // a spring-forward night, or 00:00 in the few zones that switch at midnight) moves to the
    // first valid local minute after it.
    public static DateTime LocalToUtc(this TimeZoneInfo zone, DateTime localTime)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
