using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.ResourceBlocks;

// A block that takes its window's bookings with it, when asked to (ADR-0038): while a problem
// is fixed, the room is closed and the people who had it are told why.
public sealed class BlockCancelsBookingsTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Mehmet = Guid.NewGuid();
    private static readonly Guid Zeynep = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly Mock<IInboxNotifier> _inbox = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(double hour) => Day.AddHours(hour);

    private Task BlockAsync(Guid resourceId, double from, double to, bool cancel, ICurrentUserService? by = null)
    {
        var context = _database.CreateContext();
        return TestBlocks.Handler(context, by ?? Admin, _notifier.Object, _inbox.Object)
            .Handle(new CreateResourceBlockCommand(resourceId, At(from), At(to), " Air conditioning broken ", cancel), CancellationToken.None)
            .ContinueWith(async t => { await context.DisposeAsync(); await t; }).Unwrap();
    }

    private async Task<Reservation> ReadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Asked_to_the_bookings_in_the_window_go_with_the_block_and_nothing_else()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var other = await _database.AddResourceAsync("Meeting Room A");
        var running = await _database.AddReservationAsync(room.Id, At(9), At(11), userId: Ayse);
        var later = await _database.AddReservationAsync(room.Id, At(14), At(15), userId: Mehmet);
        var after = await _database.AddReservationAsync(room.Id, At(18), At(19), userId: Mehmet);
        var elsewhere = await _database.AddReservationAsync(other.Id, At(14), At(15), userId: Mehmet);
        var gone = await _database.AddReservationAsync(room.Id, At(12), At(13), ReservationStatus.Cancelled, Ayse);

        await BlockAsync(room.Id, 10, 18, cancel: true);

        (await ReadAsync(running.Id)).Status.Should().Be(ReservationStatus.Cancelled);
        (await ReadAsync(later.Id)).Status.Should().Be(ReservationStatus.Cancelled);
        (await ReadAsync(after.Id)).Status.Should().Be(ReservationStatus.Confirmed, "it starts as the block ends");
        (await ReadAsync(elsewhere.Id)).Status.Should().Be(ReservationStatus.Confirmed);
        (await ReadAsync(gone.Id)).Status.Should().Be(ReservationStatus.Cancelled);
        await using var context = _database.CreateContext();
        var block = await context.ResourceBlocks.SingleAsync();
        block.Reason.Should().Be("Air conditioning broken");
        block.StartDateTime.Should().Be(At(10));
    }

    [Fact]
    public async Task Everyone_on_a_cancelled_booking_is_told_why_and_every_screen_frees_the_slot()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var meeting = await _database.AddReservationAsync(room.Id, At(14), At(15), userId: Ayse);
        await using (var context = _database.CreateContext())
        {
            context.ReservationAttendees.AddRange(
                new ReservationAttendee { ReservationId = meeting.Id, UserId = Mehmet },
                new ReservationAttendee { ReservationId = meeting.Id, UserId = Zeynep });
            await context.SaveChangesAsync();
        }

        await BlockAsync(room.Id, 10, 18, cancel: true);

        await using var verify = _database.CreateContext();
        var messages = await verify.InboxMessages.AsNoTracking().ToListAsync();
        messages.Select(m => m.RecipientId).Should().BeEquivalentTo([Ayse, Mehmet, Zeynep]);
        messages.Should().OnlyContain(m =>
            m.Kind == InboxMessageKind.CancelledByBlock
            && m.SenderId == Admin.UserId
            && m.ReservationId == meeting.Id
            && m.ResourceName == "Board Room"
            && m.Reason == "Air conditioning broken"
            && m.StartDateTime == At(14)
            && m.EndDateTime == At(15));
        _notifier.Verify(
            n => n.ReservationCancelledAsync(It.Is<ReservationDto>(r => r.Id == meeting.Id && r.Status == ReservationStatus.Cancelled), It.IsAny<CancellationToken>()),
            Times.Once);
        _inbox.Verify(
            n => n.MessagesSentAsync(It.Is<IReadOnlyList<InboxMessageDto>>(m => m.Count == 3), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Whoever_blocks_is_not_told_about_their_own_booking()
    {
        var room = await _database.AddResourceAsync("Board Room");
        await _database.AddReservationAsync(room.Id, At(14), At(15), userId: Admin.UserId);

        await BlockAsync(room.Id, 10, 18, cancel: true);

        await using var context = _database.CreateContext();
        (await context.InboxMessages.CountAsync()).Should().Be(0);
        _inbox.Verify(n => n.MessagesSentAsync(It.IsAny<IReadOnlyList<InboxMessageDto>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Not_asked_to_or_over_another_block_nothing_is_cancelled()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var booking = await _database.AddReservationAsync(room.Id, At(14), At(15), userId: Ayse);
        await _database.AddBlockAsync(room.Id, At(16), At(17));

        (await FluentActions.Awaiting(() => BlockAsync(room.Id, 10, 15, cancel: false)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.BlockOverlapsReservations);
        (await FluentActions.Awaiting(() => BlockAsync(room.Id, 10, 18, cancel: true)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.BlockOverlapsBlock);

        (await ReadAsync(booking.Id)).Status.Should().Be(ReservationStatus.Confirmed);
        _notifier.VerifyNoOtherCalls();
        await using var context = _database.CreateContext();
        (await context.InboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_manager_closes_their_teams_resource_but_not_a_shared_one()
    {
        var zeynep = new FakeCurrentUser(Zeynep, Roles.Manager) { TeamId = Zeynep };
        var teamRoom = await _database.AddResourceAsync("Studio");
        var shared = await _database.AddResourceAsync("Board Room");
        await using (var context = _database.CreateContext())
        {
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = teamRoom.Id, TeamId = Zeynep });
            await context.SaveChangesAsync();
        }

        var inTeam = await _database.AddReservationAsync(teamRoom.Id, At(14), At(15), userId: Ayse);
        var onShared = await _database.AddReservationAsync(shared.Id, At(14), At(15), userId: Ayse);

        await BlockAsync(teamRoom.Id, 10, 18, cancel: true, by: zeynep);
        await FluentActions.Awaiting(() => BlockAsync(shared.Id, 10, 18, cancel: true, by: zeynep))
            .Should().ThrowAsync<ForbiddenAccessException>();

        (await ReadAsync(inTeam.Id)).Status.Should().Be(ReservationStatus.Cancelled);
        (await ReadAsync(onShared.Id)).Status.Should().Be(ReservationStatus.Confirmed);
    }
}
