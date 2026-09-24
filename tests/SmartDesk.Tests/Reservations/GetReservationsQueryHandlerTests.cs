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
        return await new GetReservationsQueryHandler(context, new FakeIdentityService(names))
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
    public void Validator_RangeLongerThan31Days_Fails() =>
        new GetReservationsQueryValidator().Validate(new GetReservationsQuery(Day, Day.AddDays(32)))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetReservationsQuery.To));
}
