using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Reservations.Commands.CancelReservation;
using SmartDesk.Application.Features.Reservations.Commands.EndReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;
using SmartDesk.Application.Features.Waitlist.Commands.JoinWaitlist;
using SmartDesk.Application.Features.Waitlist.Commands.LeaveWaitlist;
using SmartDesk.Application.Features.Waitlist.Common;
using SmartDesk.Application.Features.Waitlist.Offers;
using SmartDesk.Application.Features.Waitlist.Queries.GetMyWaitlist;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Waitlist;

// Waiting for a taken time, and getting it when it frees up (ADR-0043).
public class WaitlistTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Day.AddHours(8);
    private static readonly Guid ZeynepId = Guid.NewGuid();
    private static readonly Guid OkanId = Guid.NewGuid();

    private readonly UserAccount _ayse = Person("Ayşe", Roles.Employee, ZeynepId);
    private readonly UserAccount _deniz = Person("Deniz", Roles.Employee, ZeynepId);
    private readonly UserAccount _selin = Person("Selin", Roles.Employee, OkanId);
    private UserAccount _mehmet = Person("Mehmet", Roles.Employee, ZeynepId);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly Mock<IReservationNotifier> _reservations = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly List<InboxMessageDto> _pushed = [];

    public WaitlistTests()
    {
        _accounts
            .Setup(a => a.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => People.SingleOrDefault(p => p.Id == id));
        _inbox
            .Setup(n => n.MessagesSentAsync(It.IsAny<IReadOnlyList<InboxMessageDto>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<InboxMessageDto> messages, CancellationToken _) => _pushed.AddRange(messages))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _database.Dispose();

    private IReadOnlyList<UserAccount> People => [_ayse, _mehmet, _deniz, _selin];

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private static UserAccount Person(string name, string role, Guid? managerId) =>
        new(Guid.NewGuid(), name, "Test", $"{name.ToLowerInvariant()}@company.example", role, AccountStatus.Active, managerId, null, Now, 0);

    private static FakeCurrentUser As(UserAccount person) =>
        new(person.Id, person.Role) { TeamId = person.Role == Roles.Manager ? person.Id : person.ManagerId };

    private FakeIdentityService Names() => new(People.ToDictionary(p => p.Id, p => p.DisplayName));

    private async Task<Resource> RoomAsync(Guid? team = null, bool checkIn = false)
    {
        var room = await _database.AddResourceAsync("Board Room");
        await using var context = _database.CreateContext();
        var tracked = await context.Resources.SingleAsync(r => r.Id == room.Id);
        tracked.CheckInRequired = checkIn;
        if (team is { } teamId)
        {
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = room.Id, TeamId = teamId });
        }

        await context.SaveChangesAsync();
        return tracked;
    }

    private async Task<WaitlistEntryDto> JoinAsync(UserAccount who, Resource room, DateTime start, DateTime end)
    {
        await using var context = _database.CreateContext();
        var entry = await new JoinWaitlistCommandHandler(context, As(who), _clock, TestPolicy.For(context, _clock))
            .Handle(new JoinWaitlistCommand(room.Id, start, end), CancellationToken.None);
        // People join one after another: the line has an order.
        _clock.Advance(TimeSpan.FromSeconds(1));
        return entry;
    }

    private async Task<IReadOnlyList<WaitlistEntryDto>> WaitingAsync(UserAccount who)
    {
        await using var context = _database.CreateContext();
        return await new GetMyWaitlistQueryHandler(context, As(who), _clock).Handle(new GetMyWaitlistQuery(), CancellationToken.None);
    }

    // A request, with what it frees then offered to the waiting list, as the pipeline does.
    private async Task<T> FreeingAsync<T>(Func<SmartDeskDbContext, Task<T>> request)
    {
        var freed = new FreedSlots();
        await using var context = _database.CreateContext(new FreedSlotsInterceptor(freed));
        var result = await request(context);
        var offer = new OfferFreedSlotHandler(
            context, _accounts.Object, Names(), _reservations.Object,
            TestInvitations.For(context, As(_ayse), _accounts.Object, _inbox.Object, _clock, Names()),
            _clock, TestPolicy.For(context, _clock), NullLogger<OfferFreedSlotHandler>.Instance);
        foreach (var slot in freed.TakeAll())
        {
            await offer.Handle(slot, CancellationToken.None);
        }

        return result;
    }

    private Task<ReservationDto> CancelAsync(UserAccount who, Guid reservationId) =>
        FreeingAsync(context => new CancelReservationCommandHandler(
                context, As(who), Names(), _reservations.Object, _clock, TestInvitations.For(context, As(who), clock: _clock))
            .Handle(new CancelReservationCommand(reservationId), CancellationToken.None));

    private async Task<List<Reservation>> BookingsAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations
            .Where(r => r.ResourceId == roomId && r.Status == ReservationStatus.Confirmed)
            .OrderBy(r => r.StartDateTime)
            .ToListAsync();
    }

    [Fact]
    public async Task Joining_ATakenTime_PutsYouInLine_BehindThoseWhoJoinedEarlier()
    {
        var room = await RoomAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);

        var first = await JoinAsync(_mehmet, room, At(10), At(11));
        var second = await JoinAsync(_deniz, room, At(10, 30), At(11, 30));

        first.Position.Should().Be(1);
        first.ResourceName.Should().Be("Board Room");
        first.ResourceLocation.Should().Be("2nd Floor");
        second.Position.Should().Be(2);
        (await WaitingAsync(_mehmet)).Should().ContainSingle().Which.StartDateTime.Should().Be(At(10));
    }

    [Fact]
    public async Task Joining_IsOnlyForATimeTaken_NotYourOwn_AndOnce()
    {
        var room = await RoomAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);

        var free = () => JoinAsync(_mehmet, room, At(12), At(13));
        (await free.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.WaitlistSlotFree);

        var own = () => JoinAsync(_ayse, room, At(10), At(11));
        await own.Should().ThrowAsync<ConflictException>().WithMessage("You've booked Board Room for part of that time yourself.");

        await JoinAsync(_mehmet, room, At(10), At(11));
        var again = () => JoinAsync(_mehmet, room, At(10, 30), At(11));
        (await again.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.WaitlistAlreadyWaiting);
    }

    [Fact]
    public async Task Joining_HasALimit()
    {
        var room = await RoomAsync();
        for (var hour = 9; hour < 9 + WaitlistRules.MaxPerUser + 1; hour++)
        {
            await _database.AddReservationAsync(room.Id, At(hour), At(hour + 1), userId: _ayse.Id);
        }

        for (var hour = 9; hour < 9 + WaitlistRules.MaxPerUser; hour++)
        {
            await JoinAsync(_mehmet, room, At(hour), At(hour + 1));
        }

        var oneMore = () => JoinAsync(_mehmet, room, At(19), At(20));
        await oneMore.Should().ThrowAsync<ConflictException>().WithMessage("You can wait for at most 10 times at once.*");
    }

    [Fact]
    public async Task SomeoneWithAsManyOfTheKindAsAllowed_WaitsOn_AndTheNextInLineGetsIt()
    {
        var room = await RoomAsync();
        var studio = await _database.AddResourceAsync("Studio");
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(10), At(11));
        await JoinAsync(_deniz, room, At(10), At(11));

        // Since then, Mehmet has booked another meeting room, and they're one each (ADR-0046).
        await _database.AddReservationAsync(studio.Id, At(14), At(15), userId: _mehmet.Id);
        await using (var context = _database.CreateContext())
        {
            (await context.ResourceTypes.SingleAsync(t => t.Id == ResourceTypeSeedData.MeetingRoomId)).MaxActivePerUser = 1;
            await context.SaveChangesAsync();
        }

        await CancelAsync(_ayse, ayses.Id);

        (await BookingsAsync(room.Id)).Should().ContainSingle().Which.UserId.Should().Be(_deniz.Id);
        (await WaitingAsync(_mehmet)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnotherTeamsResource_IsNotThereToWaitFor()
    {
        var room = await RoomAsync(team: ZeynepId);
        await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);

        var act = () => JoinAsync(_selin, room, At(10), At(11));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Leaving_IsForTheOneWaiting()
    {
        var room = await RoomAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        var entry = await JoinAsync(_mehmet, room, At(10), At(11));

        await using (var context = _database.CreateContext())
        {
            var notTheirs = () => new LeaveWaitlistCommandHandler(context, As(_deniz)).Handle(new LeaveWaitlistCommand(entry.Id), CancellationToken.None);
            await notTheirs.Should().ThrowAsync<NotFoundException>();
            await new LeaveWaitlistCommandHandler(context, As(_mehmet)).Handle(new LeaveWaitlistCommand(entry.Id), CancellationToken.None);
        }

        (await WaitingAsync(_mehmet)).Should().BeEmpty();
    }

    [Fact]
    public async Task ACancelledBooking_IsBookedForWhoeverWaitedLongest_AndTheyAreTold()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(10), At(11));
        await JoinAsync(_deniz, room, At(10), At(11));

        await CancelAsync(_ayse, ayses.Id);

        var booked = (await BookingsAsync(room.Id)).Should().ContainSingle().Subject;
        booked.UserId.Should().Be(_mehmet.Id);
        (booked.StartDateTime, booked.EndDateTime).Should().Be((At(10), At(11)));
        // Booked like one made in the form: with its reminder.
        booked.ReminderMinutes.Should().Be(WaitlistRules.ReminderMinutes);
        (await WaitingAsync(_mehmet)).Should().BeEmpty();
        (await WaitingAsync(_deniz)).Should().ContainSingle().Which.Position.Should().Be(1);
        _pushed.Should().ContainSingle(m => m.RecipientId == _mehmet.Id && m.Kind == InboxMessageKind.WaitlistBooked);
        _reservations.Verify(n => n.ReservationCreatedAsync(
            It.Is<ReservationDto>(r => r.UserId == _mehmet.Id && r.ResourceId == room.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenTheFirstInLineCancelsInTurn_TheNextGetsIt()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(10), At(11));
        await JoinAsync(_deniz, room, At(10), At(11));
        await CancelAsync(_ayse, ayses.Id);
        var mehmets = (await BookingsAsync(room.Id)).Single();

        await CancelAsync(_mehmet, mehmets.Id);

        (await BookingsAsync(room.Id)).Should().ContainSingle().Which.UserId.Should().Be(_deniz.Id);
    }

    [Fact]
    public async Task ATimeStillPartlyTaken_WaitsOn()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await _database.AddReservationAsync(room.Id, At(11), At(12), userId: _deniz.Id);
        await JoinAsync(_mehmet, room, At(10), At(12));

        await CancelAsync(_ayse, ayses.Id);

        (await BookingsAsync(room.Id)).Should().ContainSingle().Which.UserId.Should().Be(_deniz.Id);
        (await WaitingAsync(_mehmet)).Should().ContainSingle();
    }

    [Fact]
    public async Task TimesThatDontOverlap_AreAllBookedFromOneFreedSlot()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(9), At(12), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(9), At(10));
        await JoinAsync(_deniz, room, At(10), At(11));

        await CancelAsync(_ayse, ayses.Id);

        (await BookingsAsync(room.Id)).Select(r => r.UserId).Should().Equal(_mehmet.Id, _deniz.Id);
    }

    [Fact]
    public async Task SomeoneWhoMayNoLongerUseTheResource_DropsOut_AndTheNextGetsIt()
    {
        var room = await RoomAsync(team: ZeynepId);
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(10), At(11));
        await JoinAsync(_deniz, room, At(10), At(11));
        _mehmet = _mehmet with { ManagerId = OkanId };

        await CancelAsync(_ayse, ayses.Id);

        (await BookingsAsync(room.Id)).Should().ContainSingle().Which.UserId.Should().Be(_deniz.Id);
        (await WaitingAsync(_mehmet)).Should().BeEmpty();
    }

    [Fact]
    public async Task ANoShowRelease_OffersWhatIsLeftOfTheTime()
    {
        var room = await RoomAsync(checkIn: true);
        var ayses = await _database.AddReservationAsync(room.Id, At(8, 30), At(10), userId: _ayse.Id);
        await MarkCheckInRequiredAsync(ayses.Id);
        await JoinAsync(_mehmet, room, At(8, 30), At(10));
        _clock.SetUtcNow(new DateTimeOffset(At(8, 46).AddSeconds(30)));

        await FreeingAsync(context => new ReleaseNoShowsCommandHandler(
                context, Names(), _reservations.Object, _clock, TestInvitations.For(context, As(_ayse), clock: _clock), TestCheckInTimes.Default)
            .Handle(new ReleaseNoShowsCommand(), CancellationToken.None));

        var booked = (await BookingsAsync(room.Id)).Should().ContainSingle().Subject;
        booked.UserId.Should().Be(_mehmet.Id);
        (booked.StartDateTime, booked.EndDateTime).Should().Be((At(8, 46), At(10)));
        // Starting now: no reminder to send. The room asks for a check-in, so this booking does too.
        booked.ReminderMinutes.Should().BeNull();
        booked.CheckInRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ATimeBegunWithTooLittleLeft_IsNotBooked_AndTheEntryGoes()
    {
        var room = await RoomAsync(checkIn: true);
        var ayses = await _database.AddReservationAsync(room.Id, At(8, 30), At(9), userId: _ayse.Id);
        await MarkCheckInRequiredAsync(ayses.Id);
        await JoinAsync(_mehmet, room, At(8, 30), At(9));
        _clock.SetUtcNow(new DateTimeOffset(At(8, 46)));

        await FreeingAsync(context => new ReleaseNoShowsCommandHandler(
                context, Names(), _reservations.Object, _clock, TestInvitations.For(context, As(_ayse), clock: _clock), TestCheckInTimes.Default)
            .Handle(new ReleaseNoShowsCommand(), CancellationToken.None));

        (await BookingsAsync(room.Id)).Should().BeEmpty();
        (await WaitingAsync(_mehmet)).Should().BeEmpty();
    }

    [Fact]
    public async Task EndingEarly_OffersTheRest()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(7), At(9), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(8, 15), At(9));

        await FreeingAsync(context => new EndReservationCommandHandler(context, As(_ayse), Names(), _reservations.Object, _clock)
            .Handle(new EndReservationCommand(ayses.Id), CancellationToken.None));

        var mehmets = (await BookingsAsync(room.Id)).Single(r => r.UserId == _mehmet.Id);
        (mehmets.StartDateTime, mehmets.EndDateTime).Should().Be((At(8, 15), At(9)));
    }

    [Fact]
    public async Task MovingABooking_OffersTheTimeItLeft()
    {
        var room = await RoomAsync();
        var ayses = await _database.AddReservationAsync(room.Id, At(10), At(11), userId: _ayse.Id);
        await JoinAsync(_mehmet, room, At(10), At(11));

        await FreeingAsync(context => new UpdateReservationCommandHandler(
                context, As(_ayse), Names(), _reservations.Object, _clock, TestInvitations.For(context, As(_ayse), _accounts.Object, clock: _clock), TestPolicy.For(context, _clock), TestApprovals.For(context, _clock))
            .Handle(new UpdateReservationCommand(room.Id, At(14), At(15), null) { Id = ayses.Id }, CancellationToken.None));

        (await BookingsAsync(room.Id)).Select(r => r.UserId).Should().Equal(_mehmet.Id, _ayse.Id);
    }

    [Fact]
    public async Task LiftingABlock_OffersItsTime()
    {
        var room = await RoomAsync();
        var block = await _database.AddBlockAsync(room.Id, At(10), At(12), "Carpet cleaning");
        await JoinAsync(_mehmet, room, At(10), At(11));

        await FreeingAsync(async context =>
        {
            await new DeleteResourceBlockCommandHandler(context, new FakeCurrentUser(Guid.NewGuid(), Roles.Admin))
                .Handle(new DeleteResourceBlockCommand(block.Id), CancellationToken.None);
            return true;
        });

        (await BookingsAsync(room.Id)).Should().ContainSingle().Which.UserId.Should().Be(_mehmet.Id);
    }

    private async Task MarkCheckInRequiredAsync(Guid reservationId)
    {
        await using var context = _database.CreateContext();
        var reservation = await context.Reservations.SingleAsync(r => r.Id == reservationId);
        reservation.CheckInRequired = true;
        await context.SaveChangesAsync();
    }
}
