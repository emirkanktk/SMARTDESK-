using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class CreateReservationCommandHandlerTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Dictionary<Guid, string> Names = new() { [UserId] = "Ayşe Yılmaz" };

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private async Task<ReservationDto> CreateAsync(CreateReservationCommand command, Guid? userId = null)
    {
        await using var context = _database.CreateContext();
        var handler = new CreateReservationCommandHandler(
            context, new FakeCurrentUser(userId ?? UserId), new FakeIdentityService(Names), _notifier.Object,
            TestInvitations.For(context, new FakeCurrentUser(userId ?? UserId)), TestPolicy.For(context));
        return await handler.Handle(command, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WithAReminder_StoresIt()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");

        var dto = await CreateAsync(new CreateReservationCommand(room.Id, At(10), At(11), null, ReminderMinutes: 30));

        dto.ReminderMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Handle_FreeSlot_SavesConfirmedReservationForCallerAndNotifies()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");

        var dto = await CreateAsync(new CreateReservationCommand(room.Id, At(10), At(11), "  Sprint planning "));

        dto.ResourceName.Should().Be("Meeting Room A");
        dto.ResourceTypeId.Should().Be(room.ResourceTypeId);
        dto.ResourceTypeName.Should().Be("Meeting Room");
        dto.UserId.Should().Be(UserId);
        dto.Status.Should().Be(ReservationStatus.Confirmed);
        dto.Note.Should().Be("Sprint planning");
        dto.UserDisplayName.Should().Be("Ayşe Yılmaz");
        // The live event carries the owner's name too, so other screens can show who booked.
        _notifier.Verify(n => n.ReservationCreatedAsync(
            It.Is<ReservationDto>(r => r.Id == dto.Id && r.UserDisplayName == "Ayşe Yılmaz"),
            It.IsAny<CancellationToken>()), Times.Once);

        await using var verify = _database.CreateContext();
        (await verify.Reservations.SingleAsync()).UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Handle_OverlapsConfirmedReservation_ThrowsConflictAndSavesNothing()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30));

        // The brief's example: existing 10:00-11:30, new request 11:00-12:00.
        var act = () => CreateAsync(new CreateReservationCommand(room.Id, At(11), At(12), null));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("Meeting Room A is already reserved*");
        _notifier.VerifyNoOtherCalls();
        await using var verify = _database.CreateContext();
        (await verify.Reservations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handle_BackToBackWithExistingReservation_Succeeds()
    {
        var room = await _database.AddResourceAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11));

        var dto = await CreateAsync(new CreateReservationCommand(room.Id, At(11), At(12), null));

        dto.Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task Handle_OverlapsOnlyCancelledReservation_Succeeds()
    {
        var room = await _database.AddResourceAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30), ReservationStatus.Cancelled);

        var dto = await CreateAsync(new CreateReservationCommand(room.Id, At(11), At(12), null));

        dto.Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Theory]
    [InlineData(ResourceStatus.Inactive)]
    [InlineData(ResourceStatus.Maintenance)]
    public async Task Handle_ResourceNotActive_ThrowsConflict(ResourceStatus status)
    {
        var camera = await _database.AddResourceAsync("Camera C-1", status: status);

        var act = () => CreateAsync(new CreateReservationCommand(camera.Id, At(10), At(11), null));

        await act.Should().ThrowAsync<ConflictException>().WithMessage($"*status: {status}*");
    }

    [Fact]
    public async Task Handle_UnknownResource_ThrowsNotFound()
    {
        var act = () => CreateAsync(new CreateReservationCommand(Guid.NewGuid(), At(10), At(11), null));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_LocalTimeInput_IsStoredAsUtcAndStillClashes()
    {
        var room = await _database.AddResourceAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30));

        var act = () => CreateAsync(new CreateReservationCommand(room.Id, At(11).ToLocalTime(), At(12).ToLocalTime(), null));

        await act.Should().ThrowAsync<ConflictException>();
    }
}
