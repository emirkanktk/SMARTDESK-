using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CancelReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class CancelReservationCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();

    public void Dispose() => _database.Dispose();

    private async Task<ReservationDto> CancelAsync(Guid reservationId, ICurrentUserService caller)
    {
        await using var context = _database.CreateContext();
        var handler = new CancelReservationCommandHandler(context, caller, _notifier.Object, new FakeTimeProvider(Now));
        return await handler.Handle(new CancelReservationCommand(reservationId), CancellationToken.None);
    }

    private async Task<Guid> UpcomingReservationAsync(ReservationStatus status = ReservationStatus.Confirmed)
    {
        var room = await _database.AddResourceAsync();
        var reservation = await _database.AddReservationAsync(room.Id, NowUtc.AddHours(1), NowUtc.AddHours(2), status, OwnerId);
        return reservation.Id;
    }

    [Fact]
    public async Task Handle_OwnerCancelsUpcomingReservation_CancelsAndNotifies()
    {
        var id = await UpcomingReservationAsync();

        var dto = await CancelAsync(id, new FakeCurrentUser(OwnerId, Roles.Employee));

        dto.Status.Should().Be(ReservationStatus.Cancelled);
        _notifier.Verify(n => n.ReservationCancelledAsync(
            It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), Times.Once);
        await using var verify = _database.CreateContext();
        (await verify.Reservations.SingleAsync()).Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_AnotherEmployee_IsForbidden()
    {
        var id = await UpcomingReservationAsync();

        var act = () => CancelAsync(id, new FakeCurrentUser(Guid.NewGuid(), Roles.Employee));

        await act.Should().ThrowAsync<ForbiddenAccessException>();
        await using var verify = _database.CreateContext();
        (await verify.Reservations.SingleAsync()).Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task Handle_AdminCancelsSomeoneElsesReservation_Succeeds()
    {
        var id = await UpcomingReservationAsync();

        var dto = await CancelAsync(id, new FakeCurrentUser(Guid.NewGuid(), Roles.Admin));

        dto.Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_AlreadyCancelled_IsANoOpWithoutASecondEvent()
    {
        var id = await UpcomingReservationAsync(ReservationStatus.Cancelled);

        var dto = await CancelAsync(id, new FakeCurrentUser(OwnerId));

        dto.Status.Should().Be(ReservationStatus.Cancelled);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_ReservationAlreadyEnded_ThrowsConflict()
    {
        var room = await _database.AddResourceAsync();
        var past = await _database.AddReservationAsync(room.Id, NowUtc.AddHours(-3), NowUtc.AddHours(-2), userId: OwnerId);

        var act = () => CancelAsync(past.Id, new FakeCurrentUser(OwnerId));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_UnknownReservation_ThrowsNotFound()
    {
        var act = () => CancelAsync(Guid.NewGuid(), new FakeCurrentUser(OwnerId));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
