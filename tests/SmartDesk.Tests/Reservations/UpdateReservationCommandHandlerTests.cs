using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class UpdateReservationCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(double hour) => NowUtc.Date.AddHours(hour);

    private async Task<ReservationDto> UpdateAsync(Reservation reservation, UpdateReservationCommand command, ICurrentUserService? caller = null)
    {
        await using var context = _database.CreateContext();
        var handler = new UpdateReservationCommandHandler(
            context, caller ?? new FakeCurrentUser(OwnerId, Roles.Employee), new FakeIdentityService(),
            _notifier.Object, new FakeTimeProvider(Now));
        return await handler.Handle(command with { Id = reservation.Id }, CancellationToken.None);
    }

    private static UpdateReservationCommand Move(Reservation r, double startHour, double endHour, Guid? resourceId = null, string? note = null) =>
        new(resourceId ?? r.ResourceId, At(startHour), At(endHour), note);

    private async Task<Reservation> ReservationAsync(double startHour, double endHour, Guid? resourceId = null, ReservationStatus status = ReservationStatus.Confirmed)
    {
        resourceId ??= (await _database.AddResourceAsync($"Room {Guid.NewGuid():N}")).Id;
        return await _database.AddReservationAsync(resourceId.Value, At(startHour), At(endHour), status, OwnerId);
    }

    private async Task<Reservation> ReloadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Handle_OwnerMovesUpcomingReservationToFreeSlot_SavesAndNotifies()
    {
        var reservation = await ReservationAsync(10, 11);

        var dto = await UpdateAsync(reservation, Move(reservation, 14, 15.5, note: " moved "));

        dto.StartDateTime.Should().Be(At(14));
        dto.EndDateTime.Should().Be(At(15.5));
        dto.Note.Should().Be("moved");
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == reservation.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShiftOverlappingOnlyItsOwnOldSlot_Succeeds()
    {
        var reservation = await ReservationAsync(10, 11);

        var dto = await UpdateAsync(reservation, Move(reservation, 10.5, 11.5));

        dto.StartDateTime.Should().Be(At(10.5));
    }

    [Fact]
    public async Task Handle_MoveOntoAnotherConfirmedReservation_ThrowsConflictAndKeepsTheOldSlot()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var reservation = await ReservationAsync(10, 11, room.Id);
        await _database.AddReservationAsync(room.Id, At(13), At(14));

        var act = () => UpdateAsync(reservation, Move(reservation, 13.5, 14.5));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("Meeting Room A is already reserved*");
        (await ReloadAsync(reservation.Id)).StartDateTime.Should().Be(At(10));
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_MoveToAResourceUnderMaintenance_ThrowsConflict()
    {
        var reservation = await ReservationAsync(10, 11);
        var camera = await _database.AddResourceAsync("Camera C-1", status: ResourceStatus.Maintenance);

        var act = () => UpdateAsync(reservation, Move(reservation, 10, 11, camera.Id));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*status: Maintenance*");
    }

    [Fact]
    public async Task Handle_NoteOnlyChangeOnAResourceNowUnderMaintenance_IsAllowed()
    {
        var room = await _database.AddResourceAsync("Meeting Room A", status: ResourceStatus.Maintenance);
        var reservation = await ReservationAsync(10, 11, room.Id);

        var dto = await UpdateAsync(reservation, Move(reservation, 10, 11, note: "bring the projector"));

        dto.Note.Should().Be("bring the projector");
    }

    [Fact]
    public async Task Handle_InProgressOnAResourceNowUnderMaintenance_CanEndEarly()
    {
        var room = await _database.AddResourceAsync("Meeting Room A", status: ResourceStatus.Maintenance);
        var reservation = await ReservationAsync(8.5, 11, room.Id); // started at 08:30, now is 09:00

        var dto = await UpdateAsync(reservation, Move(reservation, 8.5, 9.5));

        dto.EndDateTime.Should().Be(At(9.5));
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == reservation.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UpcomingShortenedWithinItsSlotOnAnInactiveResource_IsAllowed()
    {
        var room = await _database.AddResourceAsync("Meeting Room A", status: ResourceStatus.Inactive);
        var reservation = await ReservationAsync(10, 12, room.Id);

        var dto = await UpdateAsync(reservation, Move(reservation, 10.5, 11.5));

        dto.StartDateTime.Should().Be(At(10.5));
        dto.EndDateTime.Should().Be(At(11.5));
    }

    [Fact]
    public async Task Handle_ExtendedOnAResourceNowUnderMaintenance_ThrowsConflictAndKeepsTheOldSlot()
    {
        // Growing past its old slot is a new claim, so an inactive resource still refuses it.
        var room = await _database.AddResourceAsync("Meeting Room A", status: ResourceStatus.Maintenance);
        var reservation = await ReservationAsync(10, 11, room.Id);

        var act = () => UpdateAsync(reservation, Move(reservation, 10, 12));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*status: Maintenance*");
        (await ReloadAsync(reservation.Id)).EndDateTime.Should().Be(At(11));
    }

    [Fact]
    public async Task Handle_InProgress_EndCanBeExtended()
    {
        var reservation = await ReservationAsync(8.5, 9.5); // started at 08:30, now is 09:00

        var dto = await UpdateAsync(reservation, Move(reservation, 8.5, 10));

        dto.EndDateTime.Should().Be(At(10));
    }

    [Fact]
    public async Task Handle_InProgress_StartOrResourceChange_ThrowsConflict()
    {
        var reservation = await ReservationAsync(8.5, 9.5);
        var otherRoom = await _database.AddResourceAsync("Meeting Room B");

        await FluentActions.Awaiting(() => UpdateAsync(reservation, Move(reservation, 8.75, 9.5)))
            .Should().ThrowAsync<ConflictException>().WithMessage("*already started*");
        await FluentActions.Awaiting(() => UpdateAsync(reservation, Move(reservation, 8.5, 9.5, otherRoom.Id)))
            .Should().ThrowAsync<ConflictException>().WithMessage("*already started*");
    }

    [Fact]
    public async Task Handle_InProgress_EndMovedIntoThePast_ThrowsConflict()
    {
        var reservation = await ReservationAsync(8, 9.5);

        var act = () => UpdateAsync(reservation, Move(reservation, 8, 8.75));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*into the past*");
    }

    [Fact]
    public async Task Handle_UpcomingMovedToAStartInThePast_IsAValidationError()
    {
        var reservation = await ReservationAsync(10, 11);

        var act = () => UpdateAsync(reservation, Move(reservation, 7, 8));

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateReservationCommand.StartDateTime));
    }

    [Fact]
    public async Task Handle_CancelledOrEnded_ThrowsConflict()
    {
        var cancelled = await ReservationAsync(10, 11, status: ReservationStatus.Cancelled);
        var ended = await ReservationAsync(6, 7);

        await FluentActions.Awaiting(() => UpdateAsync(cancelled, Move(cancelled, 12, 13)))
            .Should().ThrowAsync<ConflictException>().WithMessage("*cancelled*");
        await FluentActions.Awaiting(() => UpdateAsync(ended, Move(ended, 12, 13)))
            .Should().ThrowAsync<ConflictException>().WithMessage("*already ended*");
    }

    [Fact]
    public async Task Handle_AnotherEmployee_IsForbidden_AdminIsAllowed()
    {
        var reservation = await ReservationAsync(10, 11);

        await FluentActions.Awaiting(() => UpdateAsync(reservation, Move(reservation, 12, 13), new FakeCurrentUser(Guid.NewGuid(), Roles.Employee)))
            .Should().ThrowAsync<ForbiddenAccessException>();

        var dto = await UpdateAsync(reservation, Move(reservation, 12, 13), new FakeCurrentUser(Guid.NewGuid(), Roles.Admin));
        dto.StartDateTime.Should().Be(At(12));
        dto.UserId.Should().Be(OwnerId, "an admin editing a reservation doesn't take it over");
    }

    private async Task<Reservation> RemindedAsync(double startHour, double endHour, int minutes = 15)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}");
        return await _database.AddReservationAsync(
            room.Id, At(startHour), At(endHour), userId: OwnerId, reminderMinutes: minutes, reminderSentAt: NowUtc);
    }

    [Fact]
    public async Task Handle_ANewStart_ClearsASentReminder_SoTheNewTimeIsRemindedToo()
    {
        var reservation = await RemindedAsync(10, 11);

        await UpdateAsync(reservation, Move(reservation, 13, 14) with { ReminderMinutes = 15 });

        var stored = await ReloadAsync(reservation.Id);
        stored.ReminderMinutes.Should().Be(15);
        stored.ReminderSentAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OnlyANewEnd_KeepsASentReminder()
    {
        var reservation = await RemindedAsync(10, 11);

        await UpdateAsync(reservation, Move(reservation, 10, 12) with { ReminderMinutes = 15 });

        (await ReloadAsync(reservation.Id)).ReminderSentAt.Should().Be(NowUtc);
    }

    [Fact]
    public async Task Handle_ANewReminderTime_ClearsTheSentMark_AndBroadcasts()
    {
        var reservation = await RemindedAsync(10, 11);

        var dto = await UpdateAsync(reservation, Move(reservation, 10, 11) with { ReminderMinutes = 30 });

        dto.ReminderMinutes.Should().Be(30);
        (await ReloadAsync(reservation.Id)).ReminderSentAt.Should().BeNull();
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LeavingTheReminderOut_TurnsItOff()
    {
        var reservation = await RemindedAsync(10, 11);

        var dto = await UpdateAsync(reservation, Move(reservation, 10, 11));

        dto.ReminderMinutes.Should().BeNull();
        (await ReloadAsync(reservation.Id)).ReminderMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NothingChanged_DoesNotBroadcast()
    {
        var reservation = await ReservationAsync(10, 11);

        await UpdateAsync(reservation, Move(reservation, 10, 11));

        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UnknownReservationOrResource_ThrowsNotFound()
    {
        var reservation = await ReservationAsync(10, 11);

        await FluentActions.Awaiting(() => UpdateAsync(new Reservation { Id = Guid.NewGuid() }, Move(reservation, 12, 13)))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => UpdateAsync(reservation, Move(reservation, 12, 13, Guid.NewGuid())))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_EndNotAfterStart_Fails(int hours) =>
        new UpdateReservationCommandValidator()
            .Validate(new UpdateReservationCommand(Guid.NewGuid(), At(10), At(10 + hours), null))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateReservationCommand.EndDateTime));
}
