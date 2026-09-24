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

        return (LocalMidnightToUtc(zone, localDate), LocalMidnightToUtc(zone, localDate.AddDays(1)));
    }

    // In the few zones that switch clocks at midnight, 00:00 can be skipped that day; the day
    // then starts at the first valid local minute.
    private static DateTime LocalMidnightToUtc(TimeZoneInfo zone, DateTime localDate)
    {
        var local = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
