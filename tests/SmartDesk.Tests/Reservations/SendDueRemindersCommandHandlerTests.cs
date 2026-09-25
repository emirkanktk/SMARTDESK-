using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

public class SendDueRemindersCommandHandlerTests : IDisposable
{
    private static readonly DateTime Nine = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Nine));
    private readonly Dictionary<Guid, string> _names = new() { [OwnerId] = "Ayşe Yılmaz" };

    public void Dispose() => _database.Dispose();

    private static DateTime At(double minutesFromNine) => Nine.AddMinutes(minutesFromNine);

    private async Task<int> RunAsync()
    {
        await using var context = _database.CreateContext();
        return await new SendDueRemindersCommandHandler(
                context, new FakeIdentityService(_names), _notifier.Object, _clock)
            .Handle(new SendDueRemindersCommand(), CancellationToken.None);
    }

    private async Task<Guid> BookAsync(
        double startMinutes, int? reminder, ReservationStatus status = ReservationStatus.Confirmed)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}");
        var reservation = await _database.AddReservationAsync(
            room.Id, At(startMinutes), At(startMinutes + 60), status, OwnerId, reminder);
        return reservation.Id;
    }

    private async Task<DateTime?> SentAtAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return (await context.Reservations.SingleAsync(r => r.Id == id)).ReminderSentAt;
    }

    private void VerifyReminded(Guid id, Times times) =>
        _notifier.Verify(n => n.ReservationStartingSoonAsync(
            It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task ADueReminder_GoesOutOnce_WithTheOwnersName()
    {
        var id = await BookAsync(startMinutes: 10, reminder: 15); // due at 08:55

        (await RunAsync()).Should().Be(1);
        (await RunAsync()).Should().Be(0);

        _notifier.Verify(n => n.ReservationStartingSoonAsync(
            It.Is<ReservationDto>(r => r.Id == id && r.UserDisplayName == "Ayşe Yılmaz" && r.ReminderMinutes == 15),
            It.IsAny<CancellationToken>()), Times.Once);
        (await SentAtAsync(id)).Should().Be(Nine);
    }

    [Fact]
    public async Task AReminder_WaitsForItsMoment_AndGoesOutExactlyThen()
    {
        var id = await BookAsync(startMinutes: 20, reminder: 15); // due at 09:05

        (await RunAsync()).Should().Be(0);
        _clock.Advance(TimeSpan.FromMinutes(4));
        (await RunAsync()).Should().Be(0);
        _clock.Advance(TimeSpan.FromMinutes(1)); // 09:05 on the dot
        (await RunAsync()).Should().Be(1);

        VerifyReminded(id, Times.Once());
    }

    [Fact]
    public async Task TheLongestOption_IsFoundAnHourAhead()
    {
        var due = await BookAsync(startMinutes: 59, reminder: 60);      // due at 08:59
        var notYet = await BookAsync(startMinutes: 90, reminder: 60);   // due at 09:30

        (await RunAsync()).Should().Be(1);

        VerifyReminded(due, Times.Once());
        VerifyReminded(notYet, Times.Never());
    }

    [Fact]
    public async Task NoReminder_Cancelled_OrAlreadyStarted_IsNeverSent()
    {
        await BookAsync(startMinutes: 10, reminder: null);
        await BookAsync(startMinutes: 10, reminder: 15, ReservationStatus.Cancelled);
        await BookAsync(startMinutes: -1, reminder: 15); // started at 08:59, e.g. the job was down

        (await RunAsync()).Should().Be(0);

        _notifier.Verify(
            n => n.ReservationStartingSoonAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AReminderWhoseSentMarkWasCleared_GoesOutAgain()
    {
        var id = await BookAsync(startMinutes: 10, reminder: 15);
        await RunAsync();

        // What an update does when the start or the reminder changes.
        await using (var context = _database.CreateContext())
        {
            var reservation = await context.Reservations.SingleAsync(r => r.Id == id);
            reservation.ReminderSentAt = null;
            await context.SaveChangesAsync();
        }

        (await RunAsync()).Should().Be(1);
        VerifyReminded(id, Times.Exactly(2));
    }
}
