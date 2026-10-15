using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

// What a booking that can't be made offers instead (ADR-0042).
public class BookingSuggestionsTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(Day.AddHours(6));
    private static readonly Guid AyseId = Guid.NewGuid();
    private static readonly Guid Zeynep = Guid.NewGuid();
    private static readonly Guid Okan = Guid.NewGuid();
    private static readonly FakeCurrentUser Ayse = new(AyseId, Roles.Employee) { TeamId = Zeynep };

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private Resource _boardRoom = null!;
    private Resource _roomB = null!;

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    // Board Room (16) is taken 10:00-11:30 and Meeting Room B (4) 11:00-12:00. Meeting Room A
    // (8) and Huddle (2) are free; the Studio is Okan's team's, and a desk is another kind.
    private async Task SeedAsync()
    {
        _boardRoom = await _database.AddResourceAsync("Board Room", capacity: 16);
        _roomB = await _database.AddResourceAsync("Meeting Room B", capacity: 4);
        await _database.AddResourceAsync("Meeting Room A", capacity: 8);
        await _database.AddResourceAsync("Huddle", capacity: 2);
        var studio = await _database.AddResourceAsync("Studio", capacity: 6);
        await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, capacity: 1);
        await using (var context = _database.CreateContext())
        {
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = studio.Id, TeamId = Okan });
            await context.SaveChangesAsync();
        }

        await _database.AddReservationAsync(_boardRoom.Id, At(10), At(11, 30));
        await _database.AddReservationAsync(_roomB.Id, At(11), At(12));
    }

    private async Task<ConflictException> BookAsync(CreateReservationCommand command)
    {
        await using var context = _database.CreateContext();
        var handler = new CreateReservationCommandHandler(
            context, Ayse, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), TestInvitations.For(context, Ayse), TestPolicy.For(context, _clock), TestApprovals.For(context, _clock));
        var behavior = new BookingSuggestionsBehavior<CreateReservationCommand>(new BookingSuggester(context, Ayse, _clock, TestPolicy.For(context, _clock)));

        var act = () => behavior.Handle(command, ct => handler.Handle(command, ct), CancellationToken.None);

        return (await act.Should().ThrowAsync<ConflictException>()).Which;
    }

    private async Task<ConflictException> MoveAsync(Reservation reservation, DateTime start, DateTime end)
    {
        var command = new UpdateReservationCommand(reservation.ResourceId, start, end, null) { Id = reservation.Id };
        await using var context = _database.CreateContext();
        var handler = new UpdateReservationCommandHandler(
            context, Ayse, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), _clock, TestInvitations.For(context, Ayse, clock: _clock), TestPolicy.For(context, _clock), TestApprovals.For(context, _clock));
        var behavior = new BookingSuggestionsBehavior<UpdateReservationCommand>(new BookingSuggester(context, Ayse, _clock, TestPolicy.For(context, _clock)));

        var act = () => behavior.Handle(command, ct => handler.Handle(command, ct), CancellationToken.None);

        return (await act.Should().ThrowAsync<ConflictException>()).Which;
    }

    private static BookingSuggestions SuggestionsOf(ConflictException conflict) =>
        conflict.Suggestions.Should().BeOfType<BookingSuggestions>().Subject;

    private static IEnumerable<(DateTime, DateTime)> Times(BookingSuggestions suggestions) =>
        suggestions.Times.Select(t => (t.Start, t.End));

    private static IEnumerable<string> Names(BookingSuggestions suggestions) =>
        suggestions.Resources.Select(r => r.Name);

    [Fact]
    public async Task ATakenTime_ComesBackWithTheNearestFreeTimes_AndTheRoomsFreeThen()
    {
        await SeedAsync();

        var conflict = await BookAsync(new CreateReservationCommand(_boardRoom.Id, At(11), At(12), null));

        conflict.Code.Should().Be(ConflictCodes.ReservationOverlaps);
        conflict.Message.Should().StartWith("Board Room is already reserved");
        var suggestions = SuggestionsOf(conflict);
        Times(suggestions).Should().Equal((At(9), At(10)), (At(11, 30), At(12, 30)));
        // None as big as the Board Room: the biggest of the smaller ones first. Not the Studio,
        // another team's, nor the desk, another kind, nor Meeting Room B, taken then.
        Names(suggestions).Should().Equal("Meeting Room A", "Huddle");
        suggestions.Resources[0].Capacity.Should().Be(8);
        suggestions.Resources[0].Location.Should().Be("2nd Floor");
    }

    [Fact]
    public async Task TheOtherRooms_HoldEveryoneComing_AndThoseAsBigAsTheOneAskedForComeFirst()
    {
        await SeedAsync();
        await _database.AddResourceAsync("Focus Room", capacity: 4);
        await _database.AddResourceAsync("Team Room", capacity: 6);

        // Meeting Room B holds 4: the rooms that big, the smallest first, then the smaller ones.
        var forTwo = await BookAsync(new CreateReservationCommand(_roomB.Id, At(11), At(12), null));
        Names(SuggestionsOf(forTwo)).Should().Equal("Focus Room", "Team Room", "Meeting Room A", "Huddle");

        // With three invited, four are coming: Huddle is too small.
        var forFour = await BookAsync(new CreateReservationCommand(
            _roomB.Id, At(11), At(12), null, AttendeeIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]));
        Names(SuggestionsOf(forFour)).Should().Equal("Focus Room", "Team Room", "Meeting Room A");
    }

    [Fact]
    public async Task ABlockedTime_OffersTimesClearOfTheBlock()
    {
        await SeedAsync();
        await _database.AddBlockAsync(_boardRoom.Id, At(12), At(15), "Carpet cleaning");

        var conflict = await BookAsync(new CreateReservationCommand(_boardRoom.Id, At(12), At(13), null));

        conflict.Code.Should().Be(ConflictCodes.ResourceBlocked);
        // 11:30-12:00 is too short for an hour.
        Times(SuggestionsOf(conflict)).Should().Equal((At(9), At(10)), (At(15), At(16)));
    }

    [Fact]
    public async Task AResourceOutOfUse_OffersOnlyOthersOfItsKind()
    {
        await SeedAsync();
        var broken = await _database.AddResourceAsync("Camera C-1", ResourceTypeSeedData.CameraId, 1, ResourceStatus.Maintenance);
        await _database.AddResourceAsync("Camera C-2", ResourceTypeSeedData.CameraId, 1);

        var conflict = await BookAsync(new CreateReservationCommand(broken.Id, At(11), At(12), null));

        conflict.Code.Should().Be(ConflictCodes.ResourceUnavailable);
        var suggestions = SuggestionsOf(conflict);
        suggestions.Times.Should().BeEmpty();
        Names(suggestions).Should().Equal("Camera C-2");
    }

    [Fact]
    public async Task MovingABooking_ItsOwnSlotIsNotInTheWay()
    {
        await SeedAsync();
        var mine = await _database.AddReservationAsync(_boardRoom.Id, At(8, 30), At(9, 30), userId: AyseId);

        // 09:30-10:30 runs into 10:00. Counting her own 08:30-09:30, the hour before would be
        // 07:30; it isn't hers to keep, so it's 09:00.
        var conflict = await MoveAsync(mine, At(9, 30), At(10, 30));

        var suggestions = SuggestionsOf(conflict);
        Times(suggestions).Should().Equal((At(9), At(10)), (At(11, 30), At(12, 30)));
        Names(suggestions).Should().Equal("Meeting Room A", "Meeting Room B", "Huddle");
    }

    [Fact]
    public async Task ABookingUnderWay_IsOfferedNothingElse()
    {
        await SeedAsync();
        var mine = await _database.AddReservationAsync(_roomB.Id, At(5), At(7), userId: AyseId);
        await _database.AddReservationAsync(_roomB.Id, At(7), At(8));

        // Running on into the next booking: only the end can change, so no other time or room.
        var conflict = await MoveAsync(mine, At(5), At(7, 30));

        conflict.Code.Should().Be(ConflictCodes.ReservationOverlaps);
        var suggestions = SuggestionsOf(conflict);
        suggestions.Times.Should().BeEmpty();
        suggestions.Resources.Should().BeEmpty();
    }

    [Fact]
    public async Task AnyOtherConflict_ComesBackAsItWas()
    {
        await SeedAsync();
        var mine = await _database.AddReservationAsync(_roomB.Id, At(5), At(7), userId: AyseId);

        // A booking under way can't start at another time.
        var conflict = await MoveAsync(mine, At(5, 30), At(7));

        conflict.Code.Should().BeNull();
        conflict.Suggestions.Should().BeNull();
    }
}
