using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SmartDesk.Application.Features.Waitlist.Offers;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Waitlist;

// Which saves give time back to a resource, and when the waiting list hears of it (ADR-0043).
public class FreedSlotsTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    // Loads Meeting Room A's 10:00-11:00 booking, changes it as `change` says, and saves.
    private async Task<IReadOnlyList<SlotFreed>> FreedBy(
        Action<SmartDesk.Domain.Entities.Reservation> change, ReservationStatus status = ReservationStatus.Confirmed)
    {
        var room = await _database.AddResourceAsync();
        var booking = await _database.AddReservationAsync(room.Id, At(10), At(11), status);
        var freed = new FreedSlots();
        await using var context = _database.CreateContext(new FreedSlotsInterceptor(freed));
        change(await context.Reservations.SingleAsync(r => r.Id == booking.Id));
        await context.SaveChangesAsync();
        return freed.TakeAll();
    }

    private static (DateTime, DateTime)[] Spans(IEnumerable<SlotFreed> slots) => slots.Select(s => (s.From, s.To)).ToArray();

    [Fact]
    public async Task ACancelledBooking_GivesBackItsWholeTime()
    {
        var freed = await FreedBy(r => r.Status = ReservationStatus.Cancelled);

        Spans(freed).Should().Equal((At(10), At(11)));
    }

    [Fact]
    public async Task ABookingShortenedAtEitherEnd_GivesBackWhatItNoLongerCovers()
    {
        Spans(await FreedBy(r => r.EndDateTime = At(10, 30))).Should().Equal((At(10, 30), At(11)));
        Spans(await FreedBy(r => r.StartDateTime = At(10, 15))).Should().Equal((At(10), At(10, 15)));
    }

    [Fact]
    public async Task ABookingMovedAway_GivesBackItsOldTime_OrResource()
    {
        Spans(await FreedBy(r => (r.StartDateTime, r.EndDateTime) = (At(14), At(15)))).Should().Equal((At(10), At(11)));
        Spans(await FreedBy(r => (r.StartDateTime, r.EndDateTime) = (At(10, 30), At(11, 30)))).Should().Equal((At(10), At(10, 30)));

        var otherRoom = await _database.AddResourceAsync("Meeting Room B");
        Guid oldRoom = default;
        var freed = await FreedBy(r =>
        {
            oldRoom = r.ResourceId;
            r.ResourceId = otherRoom.Id;
        });
        freed.Should().ContainSingle().Which.Should().Be(new SlotFreed(oldRoom, At(10), At(11)));
    }

    [Fact]
    public async Task ABookingRunningLonger_OrOneAlreadyCancelled_GivesBackNothing()
    {
        (await FreedBy(r => r.EndDateTime = At(12))).Should().BeEmpty();
        (await FreedBy(r => r.Note = "Moved upstairs", ReservationStatus.Cancelled)).Should().BeEmpty();
    }

    [Fact]
    public async Task ALiftedBlock_GivesBackItsTime()
    {
        var room = await _database.AddResourceAsync();
        var block = await _database.AddBlockAsync(room.Id, At(9), At(12));
        var freed = new FreedSlots();
        await using (var context = _database.CreateContext(new FreedSlotsInterceptor(freed)))
        {
            context.ResourceBlocks.Remove(await context.ResourceBlocks.SingleAsync(b => b.Id == block.Id));
            await context.SaveChangesAsync();
        }

        freed.TakeAll().Should().Equal(new SlotFreed(room.Id, At(9), At(12)));
    }

    [Fact]
    public async Task TheWaitingListHears_OnlyOnceTheRequestHasSucceeded()
    {
        var freed = new FreedSlots();
        var publisher = new Mock<IPublisher>();
        var behavior = new OfferFreedSlotsBehavior<string, int>(freed, publisher.Object);
        var room = Guid.NewGuid();

        await behavior.Handle("cancel", _ =>
        {
            freed.Add(room, At(10), At(11));
            return Task.FromResult(1);
        }, CancellationToken.None);

        publisher.Verify(p => p.Publish(new SlotFreed(room, At(10), At(11)), It.IsAny<CancellationToken>()), Times.Once);

        var failing = () => behavior.Handle("cancel", _ =>
        {
            freed.Add(room, At(12), At(13));
            throw new InvalidOperationException("rolled back");
        }, CancellationToken.None);

        await failing.Should().ThrowAsync<InvalidOperationException>();
        publisher.Verify(p => p.Publish(It.IsAny<SlotFreed>(), It.IsAny<CancellationToken>()), Times.Once);
        freed.TakeAll().Should().BeEmpty();
    }
}
