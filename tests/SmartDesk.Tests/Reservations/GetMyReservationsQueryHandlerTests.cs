using FluentAssertions;
using SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class GetMyReservationsQueryHandlerTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Handle_ReturnsOnlyCallersReservations_IncludingCancelled_OldestFirst()
    {
        var me = Guid.NewGuid();
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, Day.AddHours(14), Day.AddHours(15), userId: me);
        await _database.AddReservationAsync(room.Id, Day.AddHours(9), Day.AddHours(10), ReservationStatus.Cancelled, me);
        await _database.AddReservationAsync(room.Id, Day.AddHours(11), Day.AddHours(12), userId: Guid.NewGuid());

        await using var context = _database.CreateContext();
        var result = await new GetMyReservationsQueryHandler(context, new FakeCurrentUser(me), new FakeIdentityService())
            .Handle(new GetMyReservationsQuery(), CancellationToken.None);

        result.Should().HaveCount(2).And.OnlyContain(r => r.UserId == me);
        result.Select(r => r.Status).Should().Equal(ReservationStatus.Cancelled, ReservationStatus.Confirmed);
        result[0].ResourceName.Should().Be("Meeting Room A");
    }
}
