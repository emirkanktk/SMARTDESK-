using FluentAssertions;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reports;

public class GetUsageReportQueryHandlerTests : IDisposable
{
    // Monday 5 October 2026; the office day is 09:00-18:00, 9 hours, five days a week.
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Friday = Monday.AddDays(4);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private readonly SqliteTestDatabase _database = new();
    private readonly Dictionary<Guid, string> _names = [];

    public void Dispose() => _database.Dispose();

    private static DateTime At(int day, double hour) =>
        Monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(day).AddHours(hour);

    private async Task<UsageReportDto> ReportAsync(DateOnly from, DateOnly to, string? timeZone = null, Guid? typeId = null)
    {
        await using var context = _database.CreateContext();
        return await new GetUsageReportQueryHandler(context, new FakeIdentityService(_names))
            .Handle(new GetUsageReportQuery(from, to, timeZone, typeId), CancellationToken.None);
    }

    [Fact]
    public async Task BookedHours_CountEverything_WorkingHours_OnlyTheOfficeDay()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(0, 8), At(0, 10));    // 2 h, 1 h of it after 09:00
        await _database.AddReservationAsync(room.Id, At(1, 10), At(1, 11.5)); // 1.5 h, all in the day

        var report = await ReportAsync(Monday, Friday);

        var row = report.Resources.Should().ContainSingle().Subject;
        row.Reservations.Should().Be(2);
        row.BookedHours.Should().Be(3.5);
        row.WorkingHoursBooked.Should().Be(2.5);
        report.WorkingHoursPerResource.Should().Be(45);
        row.Utilization.Should().Be(0.0556); // 2.5 / 45
    }

    [Fact]
    public async Task Weekends_CountInBookedHours_ButNotAgainstTheOfficeDay()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(5, 10), At(5, 12)); // Saturday

        var report = await ReportAsync(Monday, Sunday);

        report.WorkingHoursPerResource.Should().Be(45);
        report.Resources[0].BookedHours.Should().Be(2);
        report.Resources[0].WorkingHoursBooked.Should().Be(0);
    }

    [Fact]
    public async Task AReservationCrossingTheRangeEdge_CountsOnlyTheInsidePart()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(-1, 22), At(0, 10)); // Sunday 22:00 to Monday 10:00

        var row = (await ReportAsync(Monday, Friday)).Resources.Single();

        row.Reservations.Should().Be(1);
        row.BookedHours.Should().Be(10);
        row.WorkingHoursBooked.Should().Be(1);
    }

    [Fact]
    public async Task AMultiDayBooking_CountsEachOfficeDayItCovers()
    {
        var car = await _database.AddResourceAsync("Company Car", ResourceTypeSeedData.CompanyCarId, 5);
        await _database.AddReservationAsync(car.Id, At(0, 0), At(2, 0)); // Monday 00:00 to Wednesday 00:00

        var row = (await ReportAsync(Monday, Friday)).Resources.Single();

        row.BookedHours.Should().Be(48);
        row.WorkingHoursBooked.Should().Be(18);
        row.Utilization.Should().Be(0.4); // 18 / 45
    }

    [Fact]
    public async Task CancelledReservations_AreCounted_ButAddNoHours()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(0, 10), At(0, 11));
        await _database.AddReservationAsync(room.Id, At(0, 12), At(0, 13), ReservationStatus.Cancelled);

        var row = (await ReportAsync(Monday, Friday)).Resources.Single();

        row.Reservations.Should().Be(1);
        row.Cancelled.Should().Be(1);
        row.BookedHours.Should().Be(1);
    }

    [Fact]
    public async Task TheOfficeDay_IsTheCallersLocalDay()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(0, 6), At(0, 8)); // 06:00-08:00 UTC = 09:00-11:00 in Istanbul

        var istanbul = await ReportAsync(Monday, Friday, "Europe/Istanbul");
        var utc = await ReportAsync(Monday, Friday);

        istanbul.TimeZone.Should().Be("Europe/Istanbul");
        istanbul.FromUtc.Should().Be(At(-1, 21)); // local midnight, UTC+3
        istanbul.Resources[0].WorkingHoursBooked.Should().Be(2);
        utc.Resources[0].WorkingHoursBooked.Should().Be(0);
    }

    [Fact]
    public async Task ARangeAcrossTheEndOfDaylightSaving_UsesBothLocalMidnights()
    {
        await _database.AddResourceAsync("Meeting Room A");

        // Friday 30 October to Monday 2 November 2026 in New York; clocks go back on Sunday.
        var report = await ReportAsync(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2), "America/New_York");

        report.FromUtc.Should().Be(new DateTime(2026, 10, 30, 4, 0, 0, DateTimeKind.Utc)); // EDT, UTC-4
        report.ToUtc.Should().Be(new DateTime(2026, 11, 3, 5, 0, 0, DateTimeKind.Utc));    // EST, UTC-5
        report.WorkingHoursPerResource.Should().Be(18); // Friday and Monday
    }

    [Fact]
    public async Task AWeekendOnlyRange_HasNoOfficeHours_AndZeroUtilization()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(5, 10), At(5, 12));

        var report = await ReportAsync(Monday.AddDays(5), Sunday);

        report.WorkingHoursPerResource.Should().Be(0);
        report.Resources[0].Utilization.Should().Be(0);
        report.Totals.Utilization.Should().Be(0);
        report.Totals.BookedHours.Should().Be(2);
    }

    [Fact]
    public async Task ActiveResourcesAppear_OtherOnesOnlyWithActivityInTheRange()
    {
        await _database.AddResourceAsync("Idle Room");
        await _database.AddResourceAsync("Retired Room", status: ResourceStatus.Inactive);
        var camera = await _database.AddResourceAsync("Camera C-1", ResourceTypeSeedData.CameraId, 1, ResourceStatus.Maintenance);
        await _database.AddReservationAsync(camera.Id, At(0, 10), At(0, 11), ReservationStatus.Cancelled);

        var report = await ReportAsync(Monday, Friday);

        report.Resources.Select(r => r.ResourceName).Should().BeEquivalentTo("Idle Room", "Camera C-1");
        report.Resources.Single(r => r.ResourceName == "Idle Room").Reservations.Should().Be(0);
    }

    [Fact]
    public async Task TotalsAndTypes_AddUpTheRows_AndRowsAreBusiestFirst()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddResourceAsync("Meeting Room B");
        var desk = await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, 1);
        await _database.AddReservationAsync(roomA.Id, At(0, 9), At(0, 18)); // a full office day
        await _database.AddReservationAsync(desk.Id, At(1, 9), At(1, 13.5));

        var report = await ReportAsync(Monday, Friday);

        report.Resources.Select(r => r.ResourceName).Should().Equal("Meeting Room A", "Desk 3-01", "Meeting Room B");
        report.Totals.Should().Be(new UsageTotals(
            Resources: 3, Reservations: 2, Cancelled: 0, BookedHours: 13.5, WorkingHoursBooked: 13.5,
            WorkingHoursAvailable: 135, Utilization: 0.1));
        var rooms = report.ByType.Single(t => t.ResourceTypeName == "Meeting Room");
        rooms.Resources.Should().Be(2);
        rooms.WorkingHoursAvailable.Should().Be(90);
        rooms.Utilization.Should().Be(0.1); // 9 / 90
        report.ByType.Single(t => t.ResourceTypeName == "Desk").Utilization.Should().Be(0.1); // 4.5 / 45
    }

    [Fact]
    public async Task TopUsers_RankByBookedHours_WithNames_AtMostFive()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var users = Enumerable.Range(1, 6).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < users.Count; i++)
        {
            // User i books i+1 half-hours on day i, so the last user has the most time.
            await _database.AddReservationAsync(room.Id, At(i % 5, 9 + i), At(i % 5, 9 + i + 0.5 * (i + 1)), userId: users[i]);
        }
        _names[users[5]] = "Ayşe Yılmaz";

        var top = (await ReportAsync(Monday, Friday)).TopUsers;

        top.Should().HaveCount(5);
        top.Select(u => u.UserId).Should().Equal(users[5], users[4], users[3], users[2], users[1]);
        top[0].DisplayName.Should().Be("Ayşe Yılmaz");
        top[0].BookedHours.Should().Be(3);
        top[1].DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task ATypeFilter_LimitsResourcesAndUsersToThatType()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var desk = await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, 1);
        var deskUser = Guid.NewGuid();
        await _database.AddReservationAsync(room.Id, At(0, 10), At(0, 11));
        await _database.AddReservationAsync(desk.Id, At(0, 10), At(0, 12), userId: deskUser);

        var report = await ReportAsync(Monday, Friday, typeId: ResourceTypeSeedData.DeskId);

        report.Resources.Should().ContainSingle().Which.ResourceId.Should().Be(desk.Id);
        report.TopUsers.Should().ContainSingle().Which.UserId.Should().Be(deskUser);
        report.Totals.BookedHours.Should().Be(2);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-04", null, "To")]         // To before From
    [InlineData("2026-10-01", "2027-01-01", null, "To")]         // 93 days
    [InlineData("2026-10-05", "2026-10-09", "Mars/Olympus", "TimeZone")]
    public void Validator_RejectsBadRanges(string from, string to, string? timeZone, string property) =>
        new GetUsageReportQueryValidator()
            .Validate(new GetUsageReportQuery(DateOnly.Parse(from), DateOnly.Parse(to), timeZone))
            .Errors.Should().ContainSingle(e => e.PropertyName == property);

    [Theory]
    [InlineData("2026-10-05", "2026-10-05", null)]              // a single day
    [InlineData("2026-10-01", "2026-12-31", "Europe/Istanbul")] // exactly 92 days
    public void Validator_AcceptsGoodRanges(string from, string to, string? timeZone) =>
        new GetUsageReportQueryValidator()
            .Validate(new GetUsageReportQuery(DateOnly.Parse(from), DateOnly.Parse(to), timeZone))
            .IsValid.Should().BeTrue();
}
