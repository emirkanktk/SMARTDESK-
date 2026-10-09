using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Commands.SetUpDoorScreen;
using SmartDesk.Application.Features.CheckIn.Commands.TurnOffDoorScreen;
using SmartDesk.Application.Features.CheckIn.Commands.WarnBeforeRelease;
using SmartDesk.Application.Features.CheckIn.Queries.GetCheckInCode;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Application.Features.Reservations.Commands.EndReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.CheckIn;

// The door screen and its changing code, the warning before a release, who checked in, and
// ending a booking at the door (ADR-0034).
public sealed class DoorScreenTests : IDisposable
{
    // Bookings start at 10:00: check-in opens at 09:50, the warning comes at 10:10, the release at 10:15.
    private static readonly DateTime Ten = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten.AddDays(-1)));
    private readonly FakeIdentityService _names = new(new Dictionary<Guid, string>
    {
        [Ayse.UserId!.Value] = "Ayşe Yılmaz",
        [Mehmet.UserId!.Value] = "Mehmet Demir",
    });
    // The setting's default; a test can set another (ADR-0040).
    private ICheckInTimes _times = TestCheckInTimes.Default;

    public void Dispose() => _database.Dispose();

    private void ItIs(double minutesFromTen) => _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(minutesFromTen)));

    private static IFrontendLinks Links() =>
        new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200" }));

    private async Task<Resource> AddRoomAsync(bool checkIn = true)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..12]);
        await using var context = _database.CreateContext();
        var tracked = await context.Resources.SingleAsync(r => r.Id == room.Id);
        tracked.CheckInRequired = checkIn;
        await context.SaveChangesAsync();
        return tracked;
    }

    private async Task<Guid> BookAsync(Guid roomId, double startMinutes = 0, double endMinutes = 60, Guid? attendee = null)
    {
        await using var context = _database.CreateContext();
        var reservation = new Reservation
        {
            ResourceId = roomId,
            UserId = Ayse.UserId!.Value,
            StartDateTime = Ten.AddMinutes(startMinutes),
            EndDateTime = Ten.AddMinutes(endMinutes),
            Status = ReservationStatus.Confirmed,
            CheckInRequired = true,
        };
        context.Reservations.Add(reservation);
        if (attendee is { } id)
        {
            context.ReservationAttendees.Add(new ReservationAttendee { Reservation = reservation, UserId = id });
        }

        await context.SaveChangesAsync();
        return reservation.Id;
    }

    private async Task<Reservation> ReadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<string> SecretOfAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return await context.Resources.Where(r => r.Id == roomId).Select(r => r.CheckInCode).SingleAsync();
    }

    // What a screen's codes are keyed by (ADR-0039): its token's hash, never shown anywhere.
    private async Task<string> ScreenKeyOfAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return (await context.Resources.Where(r => r.Id == roomId).Select(r => r.DoorScreenTokenHash).SingleAsync())!;
    }

    private async Task<string> SetUpScreenAsync(Guid roomId, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        var link = await new SetUpDoorScreenCommandHandler(context, user ?? Admin, Links(), _clock)
            .Handle(new SetUpDoorScreenCommand(roomId), CancellationToken.None);
        return link.Url[(link.Url.IndexOf('#') + 1)..];
    }

    private async Task TurnOffScreenAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        await new TurnOffDoorScreenCommandHandler(context, Admin).Handle(new TurnOffDoorScreenCommand(roomId), CancellationToken.None);
    }

    private async Task<DoorScreenDto> ScreenAsync(Guid roomId, string? token, string? timeZone = null)
    {
        await using var context = _database.CreateContext();
        return await new GetDoorScreenQueryHandler(context, Links(), _clock)
            .Handle(new GetDoorScreenQuery(roomId, token, timeZone), CancellationToken.None);
    }

    private async Task<ReservationDto> CheckInAsync(Guid roomId, string code, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return await new CheckInCommandHandler(context, user ?? Ayse, _names, _notifier.Object, _clock, _times)
            .Handle(new CheckInCommand(code) { ResourceId = roomId }, CancellationToken.None);
    }

    private async Task<int> WarnAsync()
    {
        await using var context = _database.CreateContext();
        return await new WarnBeforeReleaseCommandHandler(context, _names, _notifier.Object, _clock, _times)
            .Handle(new WarnBeforeReleaseCommand(), CancellationToken.None);
    }

    private async Task<ReservationDto> EndAsync(Guid id, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return await new EndReservationCommandHandler(context, user ?? Ayse, _names, _notifier.Object, _clock)
            .Handle(new EndReservationCommand(id), CancellationToken.None);
    }

    private static string CodeIn(DoorScreenDto screen) => screen.CheckIn!.Url[(screen.CheckIn.Url.IndexOf("code=", StringComparison.Ordinal) + 5)..];

    // --- The changing code ---

    [Fact]
    public void The_code_changes_every_30_seconds_and_depends_on_the_screens_key()
    {
        var key = CheckInRules.HashDoorScreenToken(CheckInRules.NewDoorScreenToken());
        var at = Ten.AddSeconds(5);

        var code = CheckInRules.ChangingCodeAt(key, at);

        code.Should().MatchRegex("^[0-9a-f]{20}$");
        CheckInRules.ChangingCodeAt(key, Ten.AddSeconds(29)).Should().Be(code, "the same 30-second step");
        CheckInRules.ChangingCodeAt(key, Ten.AddSeconds(30)).Should().NotBe(code);
        CheckInRules.ChangingCodeAt(CheckInRules.HashDoorScreenToken(CheckInRules.NewDoorScreenToken()), at).Should().NotBe(code);
        CheckInRules.StepEndsAt(at).Should().Be(Ten.AddSeconds(30));
    }

    [Theory]
    [InlineData(0, true)]    // this step's
    [InlineData(-30, true)]  // the last step's: scanned just as it changed
    [InlineData(-60, false)] // older: a photo from a minute ago
    [InlineData(30, false)]  // not shown yet
    public void A_code_is_taken_during_its_step_and_the_next(int stepOffsetSeconds, bool taken)
    {
        var key = CheckInRules.HashDoorScreenToken(CheckInRules.NewDoorScreenToken());
        var now = Ten.AddSeconds(10);
        var presented = CheckInRules.ChangingCodeAt(key, now.AddSeconds(stepOffsetSeconds));

        CheckInRules.ChangingCodeMatches(key, presented, now).Should().Be(taken);
    }

    [Fact]
    public void A_screen_token_is_kept_as_its_hash_and_matched_against_it()
    {
        var token = CheckInRules.NewDoorScreenToken();
        var hash = CheckInRules.HashDoorScreenToken(token);

        token.Should().MatchRegex("^[0-9a-f]{64}$");
        hash.Should().NotBe(token).And.HaveLength(64);
        CheckInRules.DoorScreenTokenMatches(hash, token).Should().BeTrue();
        CheckInRules.DoorScreenTokenMatches(hash, CheckInRules.NewDoorScreenToken()).Should().BeFalse();
        CheckInRules.DoorScreenTokenMatches(hash, null).Should().BeFalse();
        CheckInRules.DoorScreenTokenMatches(null, token).Should().BeFalse();
    }

    // --- Setting up a screen ---

    [Fact]
    public async Task Setting_up_a_screen_gives_a_link_once_and_keeps_only_its_hash()
    {
        var room = await AddRoomAsync();
        ItIs(-60);

        var token = await SetUpScreenAsync(room.Id);

        await using var context = _database.CreateContext();
        var stored = await context.Resources.AsNoTracking().SingleAsync(r => r.Id == room.Id);
        stored.DoorScreenTokenHash.Should().Be(CheckInRules.HashDoorScreenToken(token));
        stored.DoorScreenSetUpAt.Should().Be(Ten.AddMinutes(-60));
        var code = await new GetCheckInCodeQueryHandler(context, Admin, Links())
            .Handle(new GetCheckInCodeQuery(room.Id), CancellationToken.None);
        code.DoorScreenSince.Should().Be(Ten.AddMinutes(-60));
    }

    [Fact]
    public async Task A_new_link_cuts_off_the_old_screen()
    {
        var room = await AddRoomAsync();
        var old = await SetUpScreenAsync(room.Id);

        var fresh = await SetUpScreenAsync(room.Id);

        await FluentActions.Awaiting(() => ScreenAsync(room.Id, old)).Should().ThrowAsync<NotFoundException>();
        (await ScreenAsync(room.Id, fresh)).ResourceName.Should().Be(room.Name);
    }

    [Fact]
    public async Task Only_those_who_may_edit_the_resource_set_up_a_screen()
    {
        var room = await AddRoomAsync();

        await FluentActions.Awaiting(() => SetUpScreenAsync(room.Id, Ayse)).Should().ThrowAsync<ForbiddenAccessException>();
    }

    // --- What the screen shows ---

    [Fact]
    public async Task The_screen_needs_its_own_token()
    {
        var room = await AddRoomAsync();

        await FluentActions.Awaiting(() => ScreenAsync(room.Id, CheckInRules.NewDoorScreenToken()))
            .Should().ThrowAsync<NotFoundException>("no screen is set up");
        await SetUpScreenAsync(room.Id);
        await FluentActions.Awaiting(() => ScreenAsync(room.Id, "not-the-token")).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => ScreenAsync(room.Id, null)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task The_screen_shows_todays_bookings_as_times_its_blocks_and_this_steps_code()
    {
        var room = await AddRoomAsync();
        var token = await SetUpScreenAsync(room.Id);
        await BookAsync(room.Id, -120, -60);           // over
        var now = await BookAsync(room.Id, 0, 60);
        await BookAsync(room.Id, 120, 180);
        await BookAsync(room.Id, 24 * 60, 24 * 60 + 60); // tomorrow
        await _database.AddBlockAsync(room.Id, Ten.AddHours(4), Ten.AddHours(5), "Cleaning");
        ItIs(5);

        var screen = await ScreenAsync(room.Id, token);

        screen.Now.Should().Be(Ten.AddMinutes(5));
        screen.Bookings.Select(b => b.StartDateTime).Should().Equal(Ten, Ten.AddHours(2));
        screen.Bookings.Should().AllSatisfy(b => b.CheckInNeeded.Should().BeTrue());
        screen.Blocks.Should().ContainSingle(b => b.Reason == "Cleaning");
        screen.CheckIn!.ChangesAt.Should().Be(Ten.AddMinutes(5).AddSeconds(30));
        CodeIn(screen).Should().Be(CheckInRules.ChangingCodeAt(await ScreenKeyOfAsync(room.Id), Ten.AddMinutes(5)));
        screen.CheckIn.Url.Should().StartWith($"http://localhost:4200/check-in/{room.Id}?code=");

        (await CheckInAsync(room.Id, CodeIn(screen))).Id.Should().Be(now);
        (await ScreenAsync(room.Id, token)).Bookings[0].CheckedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Today_is_the_screens_own_day()
    {
        var room = await AddRoomAsync();
        var token = await SetUpScreenAsync(room.Id);
        // 22:30 UTC is 01:30 the next day in Istanbul.
        await BookAsync(room.Id, 12 * 60 + 30, 12 * 60 + 90);
        ItIs(0);

        (await ScreenAsync(room.Id, token)).Bookings.Should().ContainSingle("it's today in UTC");
        (await ScreenAsync(room.Id, token, "Europe/Istanbul")).Bookings.Should().BeEmpty("it's tomorrow in Istanbul");
    }

    [Fact]
    public async Task Without_check_in_the_screen_shows_no_code()
    {
        var room = await AddRoomAsync(checkIn: false);
        var token = await SetUpScreenAsync(room.Id);

        (await ScreenAsync(room.Id, token)).CheckIn.Should().BeNull();
    }

    // --- Checking in while a screen is set up ---

    [Fact]
    public async Task With_a_screen_only_its_changing_code_checks_in()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        var secret = await SecretOfAsync(room.Id);
        await SetUpScreenAsync(room.Id);
        ItIs(-5);

        (await FluentActions.Awaiting(() => CheckInAsync(room.Id, secret)).Should().ThrowAsync<ForbiddenAccessException>())
            .Which.Message.Should().Contain("door screen", "the printed code no longer works");
        var key = await ScreenKeyOfAsync(room.Id);
        var stale = CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-6));
        await FluentActions.Awaiting(() => CheckInAsync(room.Id, stale)).Should().ThrowAsync<ForbiddenAccessException>();

        var current = CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-5));
        (await CheckInAsync(room.Id, current)).CheckedInAt.Should().Be(Ten.AddMinutes(-5));
    }

    // Issue #101: the screen's codes were keyed by the printed secret, so a photo of the
    // printed code, or its link, computed every screen code offline.
    [Fact]
    public async Task A_screen_code_computed_from_the_printed_secret_is_refused()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        var printed = await SecretOfAsync(room.Id);
        await SetUpScreenAsync(room.Id);
        ItIs(-5);

        var forged = CheckInRules.ChangingCodeAt(printed, Ten.AddMinutes(-5));

        await FluentActions.Awaiting(() => CheckInAsync(room.Id, forged)).Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task A_new_screen_link_changes_the_codes_and_the_old_ones_stop()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        await SetUpScreenAsync(room.Id);
        ItIs(-5);
        var before = CheckInRules.ChangingCodeAt(await ScreenKeyOfAsync(room.Id), Ten.AddMinutes(-5));

        await SetUpScreenAsync(room.Id);

        await FluentActions.Awaiting(() => CheckInAsync(room.Id, before)).Should().ThrowAsync<ForbiddenAccessException>();
        var after = CheckInRules.ChangingCodeAt(await ScreenKeyOfAsync(room.Id), Ten.AddMinutes(-5));
        (await CheckInAsync(room.Id, after)).CheckedInAt.Should().Be(Ten.AddMinutes(-5));
    }

    [Fact]
    public async Task Turning_the_screen_off_brings_the_printed_code_back()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        var token = await SetUpScreenAsync(room.Id);
        await TurnOffScreenAsync(room.Id);
        await TurnOffScreenAsync(room.Id); // again: nothing to do
        ItIs(-5);

        (await CheckInAsync(room.Id, await SecretOfAsync(room.Id))).CheckedInAt.Should().NotBeNull();
        await FluentActions.Awaiting(() => ScreenAsync(room.Id, token)).Should().ThrowAsync<NotFoundException>();
    }

    // --- Who checked in ---

    [Fact]
    public async Task The_booking_says_who_checked_it_in_until_it_moves()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, attendee: Mehmet.UserId);
        ItIs(-2);

        var dto = await CheckInAsync(room.Id, await SecretOfAsync(room.Id), Mehmet);

        dto.CheckedInBy.Should().Be(Mehmet.UserId);
        dto.CheckedInByName.Should().Be("Mehmet Demir");
        (await ReadAsync(id)).CheckedInByUserId.Should().Be(Mehmet.UserId);

        await using var context = _database.CreateContext();
        var moved = await new UpdateReservationCommandHandler(
                context, Ayse, _names, _notifier.Object, _clock, TestInvitations.For(context, Ayse))
            .Handle(new UpdateReservationCommand(room.Id, Ten.AddHours(3), Ten.AddHours(4), null, null, [Mehmet.UserId!.Value]) { Id = id }, CancellationToken.None);
        moved.CheckedInBy.Should().BeNull();
        moved.CheckedInByName.Should().BeNull();
    }

    // --- The warning before a release ---

    [Fact]
    public async Task Five_minutes_before_the_release_the_owner_and_those_invited_are_warned_once()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id, attendee: Mehmet.UserId);
        IReadOnlyCollection<Guid>? recipients = null;
        _notifier
            .Setup(n => n.ReservationReleaseWarningAsync(It.IsAny<ReservationDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback((ReservationDto _, IReadOnlyCollection<Guid> to, CancellationToken _) => recipients = to);

        ItIs(9.99);
        (await WarnAsync()).Should().Be(0);
        ItIs(10);
        (await WarnAsync()).Should().Be(1);
        (await WarnAsync()).Should().Be(0);

        recipients.Should().BeEquivalentTo([Ayse.UserId!.Value, Mehmet.UserId!.Value]);
        (await ReadAsync(id)).ReleaseWarningSentAt.Should().Be(Ten.AddMinutes(10));
    }

    [Fact]
    public async Task Nobody_is_warned_about_a_booking_checked_in_released_or_not_needing_it()
    {
        var room = await AddRoomAsync();
        var plain = await AddRoomAsync(checkIn: false);
        await BookAsync(room.Id);
        await BookAsync(plain.Id);
        ItIs(-5);
        await CheckInAsync(room.Id, await SecretOfAsync(room.Id));
        var late = await BookAsync(room.Id, 70, 130);

        ItIs(12);
        (await WarnAsync()).Should().Be(0);
        ItIs(70 + 15); // the late one's window has closed: it's the release now, not a warning
        (await WarnAsync()).Should().Be(0);
        (await ReadAsync(late)).ReleaseWarningSentAt.Should().BeNull();
    }

    [Fact]
    public async Task With_a_grace_shorter_than_five_minutes_the_warning_comes_at_the_start()
    {
        _times = TestCheckInTimes.WithGrace(1);
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);

        ItIs(-0.01);
        (await WarnAsync()).Should().Be(0, "never before the start");
        ItIs(0);
        (await WarnAsync()).Should().Be(1);

        (await ReadAsync(id)).ReleaseWarningSentAt.Should().Be(Ten);
    }

    // --- Ending at the door ---

    [Fact]
    public async Task Ending_a_booking_under_way_frees_the_room_now()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);
        ItIs(25);

        var ended = await EndAsync(id);

        ended.EndDateTime.Should().Be(Ten.AddMinutes(25));
        ended.StartDateTime.Should().Be(Ten);
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_a_booking_under_way_ends_and_only_for_its_owner_or_an_admin()
    {
        var room = await AddRoomAsync();
        var id = await BookAsync(room.Id);

        ItIs(-5);
        (await FluentActions.Awaiting(() => EndAsync(id)).Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Contain("cancel it instead");
        ItIs(0);
        await FluentActions.Awaiting(() => EndAsync(id)).Should().ThrowAsync<ConflictException>("it starts this very moment");

        ItIs(20);
        await FluentActions.Awaiting(() => EndAsync(id, Mehmet)).Should().ThrowAsync<ForbiddenAccessException>();
        (await EndAsync(id, Admin)).EndDateTime.Should().Be(Ten.AddMinutes(20));

        ItIs(30);
        await FluentActions.Awaiting(() => EndAsync(id)).Should().ThrowAsync<ConflictException>("it's over");
    }
}
