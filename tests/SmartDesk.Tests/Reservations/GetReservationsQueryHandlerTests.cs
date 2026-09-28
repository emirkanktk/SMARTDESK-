using FluentAssertions;
using SmartDesk.Application.Features.Reservations.Queries.GetReservations;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class GetReservationsQueryHandlerTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour) => Day.AddHours(hour);

    private async Task<IReadOnlyList<Application.Features.Reservations.Common.ReservationDto>> QueryAsync(
        GetReservationsQuery query, IReadOnlyDictionary<Guid, string>? names = null)
    {
        await using var context = _database.CreateContext();
        return await new GetReservationsQueryHandler(context, TestAccess.Admin, new FakeIdentityService(names))
            .Handle(query, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ReturnsConfirmedReservationsOverlappingTheRange_WithOwnerNames()
    {
        var ayse = Guid.NewGuid();
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(8), At(10), userId: ayse);          // overlaps the start
        await _database.AddReservationAsync(room.Id, At(12), At(13), userId: ayse);         // inside
        await _database.AddReservationAsync(room.Id, At(17), At(18), userId: ayse);         // starts at the range end
        await _database.AddReservationAsync(room.Id, At(11), At(12), ReservationStatus.Cancelled, ayse);

        var result = await QueryAsync(new GetReservationsQuery(At(9), At(17)), new Dictionary<Guid, string> { [ayse] = "Ayşe Yılmaz" });

        result.Select(r => r.StartDateTime.Hour).Should().Equal(8, 12);
        result.Should().OnlyContain(r => r.UserDisplayName == "Ayşe Yılmaz");
    }

    [Fact]
    public async Task Handle_FiltersByResourceAndByType()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        var roomB = await _database.AddResourceAsync("Meeting Room B");
        var desk = await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, 1);
        foreach (var resource in new[] { roomA, roomB, desk })
        {
            await _database.AddReservationAsync(resource.Id, At(10), At(11));
        }

        (await QueryAsync(new GetReservationsQuery(At(9), At(12), ResourceId: roomB.Id)))
            .Select(r => r.ResourceName).Should().Equal("Meeting Room B");
        (await QueryAsync(new GetReservationsQuery(At(9), At(12), ResourceTypeId: ResourceTypeSeedData.MeetingRoomId)))
            .Select(r => r.ResourceName).Should().Equal("Meeting Room A", "Meeting Room B");
    }

    [Fact]
    public async Task Handle_CarriesTheResourceTypeIdAndName()
    {
        // A screen filtered by type places live events by this id, without the resource list.
        var desk = await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, 1);
        await _database.AddReservationAsync(desk.Id, At(10), At(11));

        var reservation = (await QueryAsync(new GetReservationsQuery(At(9), At(12)))).Should().ContainSingle().Subject;

        reservation.ResourceTypeId.Should().Be(ResourceTypeSeedData.DeskId);
        reservation.ResourceTypeName.Should().Be("Desk");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_ToNotAfterFrom_Fails(int hours) =>
        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(At(10), At(10 + hours)))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetReservationsQuery.To));

    [Fact]
    public void Validator_SixWeekMonthGrid_Passes() =>
        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(Day, Day.AddDays(42)))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Validator_RangeLongerThan42Days_Fails() =>
        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(Day, Day.AddDays(43)))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetReservationsQuery.To)
                && e.ErrorMessage == "The range can be at most 42 days.");

    [Theory]
    [InlineData("Europe/Berlin", "2026-09-28", "2026-11-09")] // clocks go back on 25 Oct
    [InlineData("America/New_York", "2026-10-26", "2026-12-07")] // and on 1 Nov
    [InlineData("Europe/Istanbul", "2026-09-28", "2026-11-09")] // no clock change
    public void Validator_SixLocalWeeksAcrossAClockChange_Pass(string timeZone, string firstDay, string dayAfterLast)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        DateTime Midnight(string day) => TimeZoneInfo.ConvertTimeToUtc(DateTime.Parse(day), zone);

        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(Midnight(firstDay), Midnight(dayAfterLast)))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_AllowsExactlyAnHourOfSlack() =>
        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(Day, Day.AddDays(42).AddHours(1).AddMinutes(1)))
            .IsValid.Should().BeFalse();
}
