using FluentAssertions;
using SmartDesk.Application.Common.Extensions;

namespace SmartDesk.Tests.Common;

public class TimeZoneExtensionsTests
{
    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    [Fact]
    public void LocalDayBoundsUtc_Istanbul_AfterLocalMidnight_IsTheNewLocalDay()
    {
        // 01:30 on 5 Oct in Istanbul is 22:30 on 4 Oct UTC.
        var (start, end) = Zone("Europe/Istanbul").LocalDayBoundsUtc(new DateTime(2026, 10, 4, 22, 30, 0, DateTimeKind.Utc));

        start.Should().Be(new DateTime(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 10, 5, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void LocalDayBoundsUtc_DaylightSavingStartDay_Is23HoursLong()
    {
        // New York springs forward on 8 March 2026: that local day has only 23 hours.
        var (start, end) = Zone("America/New_York").LocalDayBoundsUtc(new DateTime(2026, 3, 8, 15, 0, 0, DateTimeKind.Utc));

        start.Should().Be(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc));  // 00:00 EST
        (end - start).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void LocalToUtc_ATimeTheClocksSkip_MovesToTheFirstValidMinute()
    {
        // 02:30 doesn't exist in New York on 8 March 2026 (02:00 jumps to 03:00 EDT).
        Zone("America/New_York").LocalToUtc(new DateTime(2026, 3, 8, 2, 30, 0))
            .Should().Be(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void LocalToUtc_AnOrdinaryTime_IsConverted()
    {
        Zone("Europe/Istanbul").LocalToUtc(new DateTime(2026, 10, 5, 9, 0, 0))
            .Should().Be(new DateTime(2026, 10, 5, 6, 0, 0, DateTimeKind.Utc));
    }
}
