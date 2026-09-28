using FluentAssertions;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Reservations.Queries.GetReservationById;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class GetReservationByIdQueryHandlerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Handle_ExistingReservation_ReturnsItWithOwnerName()
    {
        var owner = Guid.NewGuid();
        var room = await _database.AddResourceAsync("Meeting Room A");
        var start = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
        var reservation = await _database.AddReservationAsync(room.Id, start, start.AddHours(1), userId: owner);

        await using var context = _database.CreateContext();
        var dto = await new GetReservationByIdQueryHandler(context, TestAccess.Admin, new FakeIdentityService(new Dictionary<Guid, string> { [owner] = "Mehmet Demir" }))
            .Handle(new GetReservationByIdQuery(reservation.Id), CancellationToken.None);

        dto.ResourceName.Should().Be("Meeting Room A");
        dto.UserDisplayName.Should().Be("Mehmet Demir");
    }

    [Fact]
    public async Task Handle_UnknownId_ThrowsNotFound()
    {
        await using var context = _database.CreateContext();
        var act = () => new GetReservationByIdQueryHandler(context, TestAccess.Admin, new FakeIdentityService())
            .Handle(new GetReservationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
