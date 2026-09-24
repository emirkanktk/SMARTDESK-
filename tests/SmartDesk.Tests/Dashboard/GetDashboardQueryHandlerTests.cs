using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Dashboard;

public class GetDashboardQueryHandlerTests : IDisposable
{
    // 2026-10-05 01:30 in Istanbul (UTC+3) - still 4 October in UTC. "Today" must be the
    // user's 5 October, not the server's 4 October.
    private static readonly DateTime NowUtc = new(2026, 10, 4, 22, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime IstanbulMidnightUtc = new(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _me = Guid.NewGuid();

    public void Dispose() => _database.Dispose();

    private async Task<DashboardDto> DashboardAsync(string? timeZone = "Europe/Istanbul")
    {
        await using var context = _database.CreateContext();
        var handler = new GetDashboardQueryHandler(
            context,
            new FakeCurrentUser(_me),
            new FakeIdentityService(new Dictionary<Guid, string> { [_me] = "Ayşe Yılmaz" }),
            new FakeTimeProvider(new DateTimeOffset(NowUtc)));
        return await handler.Handle(new GetDashboardQuery(timeZone), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_TodayIsTheUsersLocalDay_NotTheServersUtcDay()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        // 4 Oct 20:00-20:30 UTC = 4 Oct 23:00-23:30 Istanbul: yesterday for the user.
        await _database.AddReservationAsync(room.Id, NowUtc.AddHours(-2.5), NowUtc.AddHours(-2));
        // 5 Oct 09:00-10:00 Istanbul.
        await _database.AddReservationAsync(room.Id, IstanbulMidnightUtc.AddHours(9), IstanbulMidnightUtc.AddHours(10));

        var dashboard = await DashboardAsync();

        dashboard.TodayStart.Should().Be(IstanbulMidnightUtc);
        dashboard.TodayEnd.Should().Be(IstanbulMidnightUtc.AddDays(1));
        dashboard.Today.Should().ContainSingle().Which.StartDateTime.Should().Be(IstanbulMidnightUtc.AddHours(9));
    }

    [Fact]
    public async Task Handle_InUseNowAndMyUpcoming_AndStats()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        var roomB = await _database.AddResourceAsync("Meeting Room B");
        await _database.AddResourceAsync("Camera C-1", status: ResourceStatus.Maintenance);
        // Running now (someone else's).
        await _database.AddReservationAsync(roomA.Id, NowUtc.AddMinutes(-30), NowUtc.AddMinutes(30));
        // Ended exactly now - not in use any more.
        await _database.AddReservationAsync(roomB.Id, NowUtc.AddHours(-1), NowUtc);
        // Mine, upcoming: six of them, plus one cancelled.
        for (var i = 1; i <= 6; i++)
        {
            await _database.AddReservationAsync(roomB.Id, NowUtc.AddHours(i), NowUtc.AddHours(i).AddMinutes(30), userId: _me);
        }
        await _database.AddReservationAsync(roomA.Id, NowUtc.AddHours(10), NowUtc.AddHours(11), ReservationStatus.Cancelled, _me);

        var dashboard = await DashboardAsync();

        dashboard.InUseNow.Should().ContainSingle().Which.ResourceName.Should().Be("Meeting Room A");
        dashboard.MyUpcoming.Should().HaveCount(GetDashboardQueryHandler.MyUpcomingLimit)
            .And.BeInAscendingOrder(r => r.StartDateTime)
            .And.OnlyContain(r => r.UserDisplayName == "Ayşe Yılmaz");
        dashboard.Stats.Should().Be(new DashboardStats(
            ActiveResources: 2, ResourcesInUseNow: 1, ReservationsToday: 8, MyUpcomingReservations: 6));
    }

    [Fact]
    public async Task Handle_NoTimeZone_UsesUtcDay()
    {
        var dashboard = await DashboardAsync(timeZone: null);

        dashboard.TimeZone.Should().Be("UTC");
        dashboard.TodayStart.Should().Be(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("Europe/Istanbul", true)]
    [InlineData("America/New_York", true)]
    [InlineData("Mars/Olympus_Mons", false)]
    public void Validator_AcceptsOnlyKnownTimeZones(string zone, bool valid) =>
        new GetDashboardQueryValidator().Validate(new GetDashboardQuery(zone)).IsValid.Should().Be(valid);
}
