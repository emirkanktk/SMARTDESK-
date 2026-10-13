using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;
using SmartDesk.Application.Features.CheckIn.Commands.ReplaceCheckInCode;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Application.Features.CheckIn.Queries.GetCheckInCode;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.CheckIn;

// Checking in at the door and releasing bookings nobody checked in to (ADR-0030).
public sealed class CheckInTests : IDisposable
{
    // Bookings in these tests start at 10:00, so the check-in is open from 09:50 until 10:15.
    private static readonly DateTime Ten = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Zeynep = Guid.NewGuid();
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    // Starts the day before: a fake clock only moves forward, and each test sets its moment.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten.AddDays(-1)));
    // The setting's default; a test can set another (ADR-0040).
    private ICheckInTimes _times = TestCheckInTimes.Default;

    public void Dispose() => _database.Dispose();

    private void ItIs(double minutesFromTen) => _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(minutesFromTen)));

    private async Task<Resource> AddRoomAsync(bool checkIn = true, Guid? team = null)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..12]);
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

    private async Task<Guid> BookAsync(
        Guid roomId,
        double startMinutes = 0,
        double endMinutes = 60,
        FakeCurrentUser? owner = null,
        bool bookedWithCheckIn = true,
        ReservationStatus status = ReservationStatus.Confirmed)
    {
        await using var context = _database.CreateContext();
        var reservation = new Reservation
        {
            ResourceId = roomId,
            UserId = (owner ?? Ayse).UserId!.Value,
            StartDateTime = Ten.AddMinutes(startMinutes),
            EndDateTime = Ten.AddMinutes(endMinutes),
            Status = status,
            CheckInRequired = bookedWithCheckIn,
        };
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();
        return reservation.Id;
    }

    private async Task<Reservation> ReadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<string> CodeOfAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return await context.Resources.Where(r => r.Id == roomId).Select(r => r.CheckInCode).SingleAsync();
    }

    private async Task<ReservationDto> CheckInAsync(Guid roomId, string? code = null, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return await new CheckInCommandHandler(context, user ?? Ayse, new FakeIdentityService(), _notifier.Object, _clock, _times)
            .Handle(new CheckInCommand(code ?? await CodeOfAsync(roomId)) { ResourceId = roomId }, CancellationToken.None);
    }

    private async Task<int> ReleaseAsync()
    {
        await using var context = _database.CreateContext();
        return await new ReleaseNoShowsCommandHandler(context, new FakeIdentityService(), _notifier.Object, _clock, TestInvitations.For(context, new FakeCurrentUser(null), clock: _clock), _times)
            .Handle(new ReleaseNoShowsCommand(), CancellationToken.None);
    }

    private static IFrontendLinks Links() =>
        new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200/" }));

    // --- Checking in ---

    [Fact]
    public async Task The_owner_checks_in_at_the_door_and_open_screens_hear_about_it()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(-5);

        var dto = await CheckInAsync(room.Id);

        dto.Id.Should().Be(id);
        dto.CheckedInAt.Should().Be(Ten.AddMinutes(-5));
        dto.CheckInRequired.Should().BeTrue();
        (await ReadAsync(id)).CheckedInAt.Should().Be(Ten.AddMinutes(-5));
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-10, true)]      // 09:50, it opens
    [InlineData(-10.001, false)] // a moment before
    [InlineData(14.999, true)]   // just before 10:15
    [InlineData(15, false)]      // 10:15, it has closed
    public async Task The_check_in_opens_ten_minutes_before_the_start_and_closes_fifteen_after(double minutes, bool open)
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(minutes);

        var act = () => CheckInAsync(room.Id);

        if (open)
        {
            await act.Should().NotThrowAsync();
            (await ReadAsync(id)).CheckedInAt.Should().NotBeNull();
        }
        else
        {
            (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
            (await ReadAsync(id)).CheckedInAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task A_short_booking_can_not_be_checked_in_once_it_is_over()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id, 0, 10);
        ItIs(10);

        var act = () => CheckInAsync(room.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Checking_in_twice_checks_in_once()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(-5);
        await CheckInAsync(room.Id);
        ItIs(2);

        var again = await CheckInAsync(room.Id);

        again.CheckedInAt.Should().Be(Ten.AddMinutes(-5), "the first check-in stands");
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_message_says_when_the_check_in_is_open()
    {
        var room = await AddRoomAsync();
        ItIs(-5);

        var act = () => CheckInAsync(room.Id);

        (await act.Should().ThrowAsync<ConflictException>()).WithMessage(
            $"You have no booking of {room.Name} to check in to right now. Check-in opens 10 minutes before the start and closes 15 minutes after it.");
    }

    [Fact]
    public async Task A_code_that_was_replaced_is_refused()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(0);

        var act = () => CheckInAsync(room.Id, CheckInRules.NewCode());

        (await act.Should().ThrowAsync<ForbiddenAccessException>()).WithMessage($"*{room.Name}*");
        (await ReadAsync(id)).CheckedInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_resource_that_does_not_use_check_in_says_so()
    {
        var room = await AddRoomAsync(checkIn: false);
        await BookAsync(room.Id);
        ItIs(0);

        var act = () => CheckInAsync(room.Id);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.CheckInNotUsed);
    }

    [Fact]
    public async Task Only_the_owner_checks_in_their_booking()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, owner: Ayse);
        ItIs(0);

        var act = () => CheckInAsync(room.Id, user: Mehmet);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
        (await ReadAsync(id)).CheckedInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_booking_of_another_resource_is_not_checked_in_by_this_code()
    {
        var booked = await AddRoomAsync();
        var elsewhere = await AddRoomAsync();
        var id = await BookAsync(booked.Id);
        ItIs(0);

        var act = () => CheckInAsync(elsewhere.Id);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
        (await ReadAsync(id)).CheckedInAt.Should().BeNull();
    }

    [Fact]
    public async Task Another_teams_resource_is_not_there()
    {
        var room = await AddRoomAsync(team: Zeynep);
        ItIs(0);

        var act = () => CheckInAsync(room.Id, user: Mehmet);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task A_booking_made_before_check_in_was_switched_on_needs_none()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id, bookedWithCheckIn: false);
        ItIs(0);

        var act = () => CheckInAsync(room.Id);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
    }

    [Fact]
    public async Task A_cancelled_booking_can_not_be_checked_in()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id, status: ReservationStatus.Cancelled);
        ItIs(0);

        var act = () => CheckInAsync(room.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Back_to_back_bookings_check_in_the_one_still_waiting_first()
    {
        var room = await AddRoomAsync();
        var first = await BookAsync(room.Id, 0, 10);
        var second = await BookAsync(room.Id, 10, 60);
        ItIs(5); // both open: the first until 10:10, the second from 10:00

        (await CheckInAsync(room.Id)).Id.Should().Be(first);
        (await CheckInAsync(room.Id)).Id.Should().Be(second);
        (await CheckInAsync(room.Id)).Id.Should().Be(first, "with both checked in, the earlier is answered again");

        (await ReadAsync(first)).CheckedInAt.Should().NotBeNull();
        (await ReadAsync(second)).CheckedInAt.Should().NotBeNull();
    }

    [Fact]
    public void The_code_is_required_and_no_longer_than_a_code()
    {
        var validator = new CheckInCommandValidator();

        validator.Validate(new CheckInCommand("")).Errors.Should().ContainSingle(e => e.PropertyName == "Code");
        validator.Validate(new CheckInCommand(new string('a', CheckInRules.CodeLength + 1))).IsValid.Should().BeFalse();
        validator.Validate(new CheckInCommand(CheckInRules.NewCode())).IsValid.Should().BeTrue();
    }

    // --- Releasing ---

    [Fact]
    public async Task A_booking_nobody_checked_in_to_is_released_when_its_check_in_closes()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);

        ItIs(14.999);
        (await ReleaseAsync()).Should().Be(0);
        ItIs(15);
        (await ReleaseAsync()).Should().Be(1);

        var released = await ReadAsync(id);
        released.Status.Should().Be(ReservationStatus.Cancelled);
        released.ReleasedAt.Should().Be(Ten.AddMinutes(15));
        _notifier.Verify(n => n.ReservationCancelledAsync(
            It.Is<ReservationDto>(r => r.Id == id && r.ReleasedAt == Ten.AddMinutes(15)), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.ReservationReleasedAsync(It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_release_happens_once()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        ItIs(20);

        (await ReleaseAsync()).Should().Be(1);
        (await ReleaseAsync()).Should().Be(0);

        _notifier.Verify(n => n.ReservationReleasedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_checked_in_booking_stays()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(3);
        await CheckInAsync(room.Id);
        ItIs(30);

        (await ReleaseAsync()).Should().Be(0);

        (await ReadAsync(id)).Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task Bookings_that_need_no_check_in_stay()
    {
        var bookedBeforeItWasOn = await BookAsync((await AddRoomAsync()).Id, bookedWithCheckIn: false);
        var switchedOffSince = await BookAsync((await AddRoomAsync(checkIn: false)).Id, bookedWithCheckIn: true);
        ItIs(30);

        (await ReleaseAsync()).Should().Be(0);

        (await ReadAsync(bookedBeforeItWasOn)).Status.Should().Be(ReservationStatus.Confirmed);
        (await ReadAsync(switchedOffSince)).Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task A_booking_says_it_needs_a_check_in_only_while_its_resource_still_asks()
    {
        var stillOn = await BookAsync((await AddRoomAsync(checkIn: true)).Id);
        var switchedOff = await BookAsync((await AddRoomAsync(checkIn: false)).Id, bookedWithCheckIn: true);

        await using var context = _database.CreateContext();
        var shown = await context.Reservations
            .Where(r => r.Id == stillOn || r.Id == switchedOff)
            .Select(ReservationDto.Projection)
            .ToDictionaryAsync(r => r.Id, r => r.CheckInRequired);

        shown[stillOn].Should().BeTrue();
        shown[switchedOff].Should().BeFalse("its resource doesn't ask any more, so there's nothing to do at the door");
    }

    [Fact]
    public async Task A_booking_that_is_over_is_left_as_it_was()
    {
        var room = await AddRoomAsync();
        var shortOne = await BookAsync(room.Id, 0, 10);
        var missedWhileTheJobWasDown = await BookAsync(room.Id, 60, 120);
        ItIs(121);

        (await ReleaseAsync()).Should().Be(0);

        (await ReadAsync(shortOne)).Status.Should().Be(ReservationStatus.Confirmed);
        (await ReadAsync(missedWhileTheJobWasDown)).ReleasedAt.Should().BeNull();
    }

    // --- The grace is a setting (ADR-0040) ---

    [Fact]
    public async Task A_one_minute_grace_closes_the_check_in_and_releases_a_minute_after_the_start()
    {
        _times = TestCheckInTimes.WithGrace(1);
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);

        ItIs(0.999);
        (await ReleaseAsync()).Should().Be(0);
        ItIs(1);
        (await FluentActions.Awaiting(() => CheckInAsync(room.Id)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
        (await ReleaseAsync()).Should().Be(1);

        (await ReadAsync(id)).ReleasedAt.Should().Be(Ten.AddMinutes(1));
    }

    [Fact]
    public async Task The_message_gives_the_grace_in_use()
    {
        _times = TestCheckInTimes.WithGrace(1);
        var room = await AddRoomAsync();
        ItIs(-5);

        var act = () => CheckInAsync(room.Id);

        (await act.Should().ThrowAsync<ConflictException>()).WithMessage("*opens 10 minutes before the start and closes 1 minute*");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(15, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void The_grace_is_from_one_minute_to_an_hour(int minutes, bool valid) =>
        new CheckInOptions { GraceMinutes = minutes }.HasValidGrace.Should().Be(valid);

    [Theory]
    [InlineData(14.999, true)]
    [InlineData(15, false)]
    public async Task A_check_in_and_a_release_are_never_both_allowed(double minutes, bool checkInAllowed)
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(minutes);

        var checkIn = () => CheckInAsync(room.Id);
        if (checkInAllowed)
        {
            await checkIn.Should().NotThrowAsync();
            (await ReleaseAsync()).Should().Be(0);
            (await ReadAsync(id)).Status.Should().Be(ReservationStatus.Confirmed);
        }
        else
        {
            await checkIn.Should().ThrowAsync<ConflictException>();
            (await ReleaseAsync()).Should().Be(1);
        }
    }

    // --- What a booking takes on from its resource ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_new_booking_takes_on_whether_its_resource_asks_for_a_check_in(bool checkIn)
    {
        var room = await AddRoomAsync(checkIn);

        ReservationDto dto;
        await using (var context = _database.CreateContext())
        {
            dto = await new CreateReservationCommandHandler(context, Ayse, new FakeIdentityService(), _notifier.Object, TestInvitations.For(context, Ayse), TestPolicy.For(context, _clock))
                .Handle(new CreateReservationCommand(room.Id, Ten, Ten.AddHours(1), null), CancellationToken.None);
        }

        dto.CheckInRequired.Should().Be(checkIn);
        (await ReadAsync(dto.Id)).CheckInRequired.Should().Be(checkIn);
    }

    [Fact]
    public async Task Moving_a_booking_takes_on_the_new_resources_setting_and_undoes_its_check_in()
    {
        var withCheckIn = await AddRoomAsync(checkIn: true);
        var without = await AddRoomAsync(checkIn: false);
        var id = await BookAsync(withCheckIn.Id);
        ItIs(-5);
        await CheckInAsync(withCheckIn.Id);

        var moved = await UpdateAsync(id, without.Id, Ten, Ten.AddHours(1));

        moved.CheckInRequired.Should().BeFalse();
        moved.CheckedInAt.Should().BeNull("that check-in was at another door");
        (await ReadAsync(id)).CheckInRequired.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_start_undoes_the_check_in()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(-5);
        await CheckInAsync(room.Id);

        var moved = await UpdateAsync(id, room.Id, Ten.AddHours(2), Ten.AddHours(3));

        moved.CheckedInAt.Should().BeNull("it was for a visit that's no longer happening");
        moved.CheckInRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Moving_a_booking_made_before_check_in_was_on_makes_it_need_one()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, bookedWithCheckIn: false);
        ItIs(-30);

        var moved = await UpdateAsync(id, room.Id, Ten.AddHours(2), Ten.AddHours(3));

        moved.CheckInRequired.Should().BeTrue("it was moved knowing the door asks for one now");
    }

    [Fact]
    public async Task Moving_to_a_room_that_asks_for_a_check_in_makes_the_booking_need_one()
    {
        var plain = await AddRoomAsync(checkIn: false);
        var withCheckIn = await AddRoomAsync();
        var id = await BookAsync(plain.Id, bookedWithCheckIn: false);
        ItIs(-30);

        var moved = await UpdateAsync(id, withCheckIn.Id, Ten, Ten.AddHours(1));

        moved.CheckInRequired.Should().BeTrue("the new room's door asks for one");
    }

    [Fact]
    public async Task Staying_longer_in_a_booking_made_before_check_in_was_on_needs_no_check_in()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, bookedWithCheckIn: false);
        // In the meeting, its check-in window long closed.
        ItIs(30);

        var longer = await UpdateAsync(id, room.Id, Ten, Ten.AddMinutes(90));

        longer.CheckInRequired.Should().BeFalse("it's the same visit, booked when the door asked for nothing");
        (await ReleaseAsync()).Should().Be(0);
        (await ReadAsync(id)).Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task A_later_end_alone_keeps_what_the_booking_was_made_with()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, bookedWithCheckIn: false);
        ItIs(-120);

        var longer = await UpdateAsync(id, room.Id, Ten, Ten.AddMinutes(90));

        longer.CheckInRequired.Should().BeFalse("only a new start or another room is a new visit");
    }

    [Fact]
    public async Task Ending_early_keeps_the_check_in()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(5);
        await CheckInAsync(room.Id);
        ItIs(20);

        var shortened = await UpdateAsync(id, room.Id, Ten, Ten.AddMinutes(30));

        shortened.CheckedInAt.Should().Be(Ten.AddMinutes(5));
    }

    [Fact]
    public async Task Check_in_is_switched_on_and_off_with_the_rest_of_the_resource()
    {
        var admin = new FakeCurrentUser(Guid.NewGuid(), Roles.Admin);
        var reader = new ResourceReader(new FakeIdentityService());

        ResourceDto created;
        await using (var context = _database.CreateContext())
        {
            created = await new CreateResourceCommandHandler(context, admin, Mock.Of<IUserAccounts>(), reader)
                .Handle(
                    new CreateResourceCommand("Board Room", ResourceTypeSeedData.MeetingRoomId, 12, "5th Floor", null, null, CheckInRequired: true),
                    CancellationToken.None);
        }

        created.CheckInRequired.Should().BeTrue();

        await using (var context = _database.CreateContext())
        {
            var updated = await new UpdateResourceCommandHandler(context, admin, reader, _clock)
                .Handle(
                    new UpdateResourceCommand("Board Room", ResourceTypeSeedData.MeetingRoomId, 12, "5th Floor", null, null, ResourceStatus.Active)
                    {
                        Id = created.Id,
                    },
                    CancellationToken.None);
            updated.CheckInRequired.Should().BeFalse("a PUT replaces every field, and leaving it out means off");
        }
    }

    private async Task<ReservationDto> UpdateAsync(Guid id, Guid roomId, DateTime start, DateTime end)
    {
        await using var context = _database.CreateContext();
        return await new UpdateReservationCommandHandler(context, Ayse, new FakeIdentityService(), _notifier.Object, _clock, TestInvitations.For(context, Ayse, clock: _clock), TestPolicy.For(context, _clock))
            .Handle(new UpdateReservationCommand(roomId, start, end, null) { Id = id }, CancellationToken.None);
    }

    // --- The QR code ---

    [Fact]
    public async Task Every_resource_has_its_own_secret_code()
    {
        var one = await CodeOfAsync((await AddRoomAsync()).Id);
        var other = await CodeOfAsync((await AddRoomAsync()).Id);

        one.Should().MatchRegex("^[0-9a-f]{32}$");
        other.Should().NotBe(one);
    }

    [Fact]
    public async Task The_code_and_its_link_are_for_whoever_may_edit_the_resource()
    {
        var shared = await AddRoomAsync();
        var zeyneps = await AddRoomAsync(team: Zeynep);
        var admin = new FakeCurrentUser(Guid.NewGuid(), Roles.Admin);
        var manager = new FakeCurrentUser(Zeynep, Roles.Manager) { TeamId = Zeynep };
        var member = new FakeCurrentUser(Guid.NewGuid(), Roles.Employee) { TeamId = Zeynep };

        async Task<CheckInCodeDto> Get(FakeCurrentUser user, Guid id)
        {
            await using var context = _database.CreateContext();
            return await new GetCheckInCodeQueryHandler(context, user, Links())
                .Handle(new GetCheckInCodeQuery(id), CancellationToken.None);
        }

        var forAdmin = await Get(admin, shared.Id);
        forAdmin.Code.Should().Be(await CodeOfAsync(shared.Id));
        forAdmin.Url.Should().Be($"http://localhost:4200/check-in/{shared.Id}?code={forAdmin.Code}");
        (await Get(manager, zeyneps.Id)).Code.Should().Be(await CodeOfAsync(zeyneps.Id));

        await FluentActions.Awaiting(() => Get(manager, shared.Id)).Should().ThrowAsync<ForbiddenAccessException>();
        await FluentActions.Awaiting(() => Get(member, zeyneps.Id)).Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task A_replaced_code_retires_the_printed_one()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        var printed = await CodeOfAsync(room.Id);

        CheckInCodeDto replaced;
        await using (var context = _database.CreateContext())
        {
            replaced = await new ReplaceCheckInCodeCommandHandler(context, new FakeCurrentUser(Guid.NewGuid(), Roles.Admin), Links())
                .Handle(new ReplaceCheckInCodeCommand(room.Id), CancellationToken.None);
        }

        replaced.Code.Should().NotBe(printed).And.Be(await CodeOfAsync(room.Id));
        ItIs(0);
        await FluentActions.Awaiting(() => CheckInAsync(room.Id, printed)).Should().ThrowAsync<ForbiddenAccessException>();
        (await CheckInAsync(room.Id, replaced.Code)).CheckedInAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Only_whoever_may_edit_the_resource_replaces_its_code()
    {
        var room = await AddRoomAsync();
        var printed = await CodeOfAsync(room.Id);

        await using var context = _database.CreateContext();
        var act = () => new ReplaceCheckInCodeCommandHandler(context, new FakeCurrentUser(Zeynep, Roles.Manager) { TeamId = Zeynep }, Links())
            .Handle(new ReplaceCheckInCodeCommand(room.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenAccessException>("a shared resource is the Admins' to manage");
        (await CodeOfAsync(room.Id)).Should().Be(printed);
    }
}
