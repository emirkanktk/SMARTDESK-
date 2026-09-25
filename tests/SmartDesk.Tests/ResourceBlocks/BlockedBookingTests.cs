using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Domain.Constants;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.ResourceBlocks;

// Business rule 7: a block makes its window unbookable everywhere a booking can be made or
// looked for - create, move, and the availability search.
public class BlockedBookingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Day = Now.UtcDateTime.Date;
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(double hour) => Day.AddHours(hour);

    private async Task CreateAsync(Guid resourceId, double startHour, double endHour)
    {
        await using var context = _database.CreateContext();
        await new CreateReservationCommandHandler(
                context, new FakeCurrentUser(UserId), new FakeIdentityService(), Mock.Of<IReservationNotifier>())
            .Handle(new CreateReservationCommand(resourceId, At(startHour), At(endHour), null), CancellationToken.None);
    }

    [Fact]
    public async Task Create_InsideABlock_ThrowsConflictWithTheReason()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddBlockAsync(room.Id, At(10), At(14), "Projector replacement");

        var act = () => CreateAsync(room.Id, 13, 15);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Meeting Room A is blocked for part of that time: Projector replacement");
    }

    [Fact]
    public async Task Create_RightAfterABlock_Succeeds()
    {
        var room = await _database.AddResourceAsync();
        await _database.AddBlockAsync(room.Id, At(10), At(14));

        await FluentActions.Awaiting(() => CreateAsync(room.Id, 14, 15)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Update_MovingIntoABlock_ThrowsConflict()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var reservation = await _database.AddReservationAsync(room.Id, At(8), At(9), userId: UserId);
        await _database.AddBlockAsync(room.Id, At(10), At(14), "Deep cleaning");

        await using var context = _database.CreateContext();
        var handler = new UpdateReservationCommandHandler(
            context, new FakeCurrentUser(UserId, Roles.Employee), new FakeIdentityService(),
            Mock.Of<IReservationNotifier>(), new FakeTimeProvider(Now));
        var act = () => handler.Handle(
            new UpdateReservationCommand(room.Id, At(11), At(12), null) { Id = reservation.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*blocked*Deep cleaning");
    }

    [Fact]
    public async Task Availability_ExcludesAResourceBlockedForPartOfTheWindow()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddResourceAsync("Meeting Room B");
        await _database.AddBlockAsync(roomA.Id, At(11), At(12));

        await using var context = _database.CreateContext();
        var available = await new GetAvailableResourcesQueryHandler(context)
            .Handle(new GetAvailableResourcesQuery(At(10), At(13)), CancellationToken.None);

        available.Select(r => r.Name).Should().Equal("Meeting Room B");
    }
}
