using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Commands.SetUpDoorScreen;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorVisit;
using SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;
using SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.CheckIn;

// The page a resource's QR code opens for someone without an account (ADR-0050): what it shows,
// checking in whoever's booking is waiting, and reporting a problem, all with the code alone.
public sealed class DoorVisitTests : IDisposable
{
    // Bookings start at 10:00: check-in opens at 09:50 and closes at 10:15.
    private static readonly DateTime Ten = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);
    // Someone at the door without an account.
    private static readonly FakeCurrentUser Nobody = new(null);

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly Mock<IIssueNotifier> _issues = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten.AddDays(-1)));
    private readonly FakeIdentityService _names = new(new Dictionary<Guid, string>
    {
        [Ayse.UserId!.Value] = "Ayşe Yılmaz",
        [Mehmet.UserId!.Value] = "Mehmet Demir",
    });

    public void Dispose() => _database.Dispose();

    private void ItIs(double minutesFromTen) => _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(minutesFromTen)));

    private static IFrontendLinks Links() =>
        new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200" }));

    private async Task<Resource> AddRoomAsync(bool checkIn = true, Guid? team = null)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..12], features: ["Whiteboard", "TV"]);
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

    private async Task<Guid> BookAsync(Guid roomId, double startMinutes = 0, double endMinutes = 60)
    {
        await using var context = _database.CreateContext();
        var reservation = new Reservation
        {
            ResourceId = roomId,
            UserId = Ayse.UserId!.Value,
            StartDateTime = Ten.AddMinutes(startMinutes),
            EndDateTime = Ten.AddMinutes(endMinutes),
            Status = ReservationStatus.Confirmed,
            Note = "Quarterly figures with Ayşe",
            CheckInRequired = true,
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

    private async Task<string> SecretOfAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return await context.Resources.Where(r => r.Id == roomId).Select(r => r.CheckInCode).SingleAsync();
    }

    // Sets up a door screen and returns what its codes are keyed by (ADR-0039).
    private async Task<string> SetUpScreenAsync(Guid roomId)
    {
        await using (var context = _database.CreateContext())
        {
            await new SetUpDoorScreenCommandHandler(context, Admin, Links(), _clock)
                .Handle(new SetUpDoorScreenCommand(roomId), CancellationToken.None);
        }

        await using var read = _database.CreateContext();
        return (await read.Resources.Where(r => r.Id == roomId).Select(r => r.DoorScreenTokenHash).SingleAsync())!;
    }

    private async Task<DoorVisitDto> VisitAsync(Guid roomId, string? code)
    {
        await using var context = _database.CreateContext();
        return await new GetDoorVisitQueryHandler(context, _clock)
            .Handle(new GetDoorVisitQuery(roomId, code), CancellationToken.None);
    }

    private async Task<ReservationDto> CheckInAtDoorAsync(Guid roomId, string code, FakeCurrentUser? user = null, bool atTheDoor = true)
    {
        await using var context = _database.CreateContext();
        return await new CheckInCommandHandler(context, user ?? Nobody, _names, _notifier.Object, _clock, TestCheckInTimes.Default)
            .Handle(new CheckInCommand(code) { ResourceId = roomId, AtTheDoor = atTheDoor }, CancellationToken.None);
    }

    private async Task<IssueReportDto> ReportAtDoorAsync(
        Guid roomId, string? code, IssueCategory category = IssueCategory.Display, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return await new CreateIssueReportCommandHandler(context, user ?? Nobody, new IssueReader(_names), _issues.Object, _clock)
            .Handle(
                new CreateIssueReportCommand(roomId, category, "The TV doesn't turn on", false, null) { DoorCode = code },
                CancellationToken.None);
    }

    private async Task AddIssueAsync(Guid roomId, IssueStatus status, Guid reporter, string description)
    {
        await using var context = _database.CreateContext();
        context.IssueReports.Add(new IssueReport
        {
            Id = Guid.CreateVersion7(),
            ResourceId = roomId,
            ReportedByUserId = reporter,
            Category = IssueCategory.Projector,
            Description = description,
            IsBlocking = true,
            Status = status,
            CreatedAt = Ten.AddDays(-1),
            ResolvedAt = status == IssueStatus.Resolved ? Ten.AddHours(-1) : null,
        });
        await context.SaveChangesAsync();
    }

    // --- Which codes open the page ---

    [Fact]
    public void A_screen_code_opens_the_page_for_half_an_hour_but_checks_in_for_a_minute()
    {
        var key = CheckInRules.HashDoorScreenToken(CheckInRules.NewDoorScreenToken());

        var twentyMinutesOld = CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-20));
        CheckInRules.ChangingCodeSeenWithin(key, twentyMinutesOld, Ten, CheckInRules.ScreenCodeOpensPageFor).Should().BeTrue();
        CheckInRules.ChangingCodeMatches(key, twentyMinutesOld, Ten).Should().BeFalse("a check-in takes this step's code or the last one's");

        var tooOld = CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-31));
        CheckInRules.ChangingCodeSeenWithin(key, tooOld, Ten, CheckInRules.ScreenCodeOpensPageFor).Should().BeFalse();
        var notShownYet = CheckInRules.ChangingCodeAt(key, Ten.AddSeconds(30));
        CheckInRules.ChangingCodeSeenWithin(key, notShownYet, Ten, CheckInRules.ScreenCodeOpensPageFor).Should().BeFalse();
    }

    [Fact]
    public void The_printed_code_or_a_recent_screen_code_shows_someone_was_at_the_door()
    {
        var printed = CheckInRules.NewCode();
        var key = CheckInRules.HashDoorScreenToken(CheckInRules.NewDoorScreenToken());
        var screenCode = CheckInRules.ChangingCodeAt(key, Ten);

        CheckInRules.SeenAtTheDoor(printed, null, printed, Ten).Should().BeTrue();
        CheckInRules.SeenAtTheDoor(printed, null, CheckInRules.NewCode(), Ten).Should().BeFalse();
        CheckInRules.SeenAtTheDoor(printed, null, null, Ten).Should().BeFalse();
        CheckInRules.SeenAtTheDoor(printed, null, screenCode, Ten).Should().BeFalse("there's no screen");
        CheckInRules.SeenAtTheDoor(printed, key, screenCode, Ten).Should().BeTrue();
        CheckInRules.SeenAtTheDoor(printed, key, printed, Ten).Should().BeTrue("the printed code only stops checking in");
    }

    // --- The page ---

    [Fact]
    public async Task The_page_shows_the_room_and_the_rest_of_its_day_with_nobodys_name()
    {
        var room = await AddRoomAsync();
        var first = await BookAsync(room.Id);
        await BookAsync(room.Id, 240, 300);
        await using (var context = _database.CreateContext())
        {
            context.ResourceBlocks.Add(new ResourceBlock
            {
                ResourceId = room.Id,
                StartDateTime = Ten.AddHours(2),
                EndDateTime = Ten.AddHours(3),
                Reason = "Deep cleaning",
                CreatedByUserId = Admin.UserId!.Value,
            });
            await context.SaveChangesAsync();
        }

        await AddIssueAsync(room.Id, IssueStatus.Open, Mehmet.UserId!.Value, "No signal from HDMI");
        await AddIssueAsync(room.Id, IssueStatus.Resolved, Mehmet.UserId!.Value, "Fixed long ago");
        ItIs(-5);
        await CheckInAtDoorAsync(room.Id, await SecretOfAsync(room.Id));

        var visit = await VisitAsync(room.Id, await SecretOfAsync(room.Id));

        visit.ResourceName.Should().Be(room.Name);
        visit.ResourceTypeName.Should().Be("Meeting Room");
        visit.Location.Should().Be("2nd Floor");
        visit.Features.Should().Equal("TV", "Whiteboard");
        visit.Status.Should().Be(ResourceStatus.Active);
        visit.CheckInRequired.Should().BeTrue();
        visit.Now.Should().Be(Ten.AddMinutes(-5));
        visit.Bookings.Should().HaveCount(2);
        visit.Bookings[0].StartDateTime.Should().Be(Ten);
        visit.Bookings[0].CheckInNeeded.Should().BeTrue();
        visit.Bookings[0].CheckedIn.Should().BeTrue();
        visit.Bookings[1].CheckedIn.Should().BeFalse();
        visit.Blocks.Should().ContainSingle().Which.Reason.Should().Be("Deep cleaning");
        var issue = visit.OpenIssues.Should().ContainSingle().Subject;
        issue.Description.Should().Be("No signal from HDMI");
        issue.IsBlocking.Should().BeTrue();

        // A corridor is no place for names: no owner, no note, no reporter, no ids of people.
        var json = JsonSerializer.Serialize(visit);
        json.Should().NotContain("Ayşe").And.NotContain("Mehmet").And.NotContain("Quarterly")
            .And.NotContain(Ayse.UserId!.Value.ToString()).And.NotContain(Mehmet.UserId!.Value.ToString())
            .And.NotContain(first.ToString());
    }

    [Fact]
    public async Task A_wrong_or_missing_code_is_the_same_404_as_no_resource()
    {
        var room = await AddRoomAsync();
        var code = await SecretOfAsync(room.Id);

        await FluentActions.Awaiting(() => VisitAsync(room.Id, CheckInRules.NewCode())).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => VisitAsync(room.Id, null)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => VisitAsync(Guid.NewGuid(), code)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task With_a_door_screen_its_codes_open_the_page_for_half_an_hour_and_the_printed_one_still_does()
    {
        var room = await AddRoomAsync();
        ItIs(-60);
        var key = await SetUpScreenAsync(room.Id);
        ItIs(0);

        (await VisitAsync(room.Id, CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-20)))).ResourceName.Should().Be(room.Name);
        (await VisitAsync(room.Id, await SecretOfAsync(room.Id))).ResourceName.Should().Be(room.Name);
        await FluentActions.Awaiting(() => VisitAsync(room.Id, CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-40))))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Another_teams_room_opens_and_checks_in_for_whoever_has_its_code()
    {
        var room = await AddRoomAsync(team: Guid.NewGuid());
        var booking = await BookAsync(room.Id);
        var code = await SecretOfAsync(room.Id);
        ItIs(-5);

        (await VisitAsync(room.Id, code)).ResourceName.Should().Be(room.Name);
        (await CheckInAtDoorAsync(room.Id, code)).Id.Should().Be(booking);
    }

    // --- Checking in at the door ---

    [Fact]
    public async Task Whoever_is_at_the_door_checks_in_the_booking_waiting_for_it_once()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(room.Id);
        var code = await SecretOfAsync(room.Id);
        ItIs(2);

        var checkedIn = await CheckInAtDoorAsync(room.Id, code);
        var again = await CheckInAtDoorAsync(room.Id, code);

        checkedIn.Id.Should().Be(booking);
        checkedIn.CheckedInAt.Should().Be(Ten.AddMinutes(2));
        again.CheckedInAt.Should().Be(Ten.AddMinutes(2));
        var stored = await ReadAsync(booking);
        stored.CheckedInAt.Should().Be(Ten.AddMinutes(2));
        stored.CheckedInByUserId.Should().BeNull("nobody signed in did it");
        _notifier.Verify(n => n.ReservationUpdatedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signed_in_at_the_door_whoever_checks_in_is_remembered()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(room.Id);
        ItIs(0);

        await CheckInAtDoorAsync(room.Id, await SecretOfAsync(room.Id), Mehmet);

        (await ReadAsync(booking)).CheckedInByUserId.Should().Be(Mehmet.UserId!.Value);
    }

    [Fact]
    public async Task With_nothing_waiting_the_door_says_so()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id, 60, 120);
        var code = await SecretOfAsync(room.Id);
        ItIs(0);

        var act = () => CheckInAtDoorAsync(room.Id, code);

        var thrown = await act.Should().ThrowAsync<ConflictException>();
        thrown.Which.Code.Should().Be(ConflictCodes.NothingToCheckIn);
        thrown.WithMessage($"*{room.Name}*");
    }

    [Fact]
    public async Task At_the_door_a_wrong_code_gives_nothing_away_and_a_room_without_check_in_says_so()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        var plain = await AddRoomAsync(checkIn: false);
        ItIs(0);

        // Not even that the room uses no check-in, which would name it.
        await FluentActions.Awaiting(() => CheckInAtDoorAsync(room.Id, CheckInRules.NewCode()))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => CheckInAtDoorAsync(plain.Id, CheckInRules.NewCode()))
            .Should().ThrowAsync<NotFoundException>();
        (await FluentActions.Awaiting(async () => await CheckInAtDoorAsync(plain.Id, await SecretOfAsync(plain.Id)))
            .Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.CheckInNotUsed);
    }

    [Fact]
    public async Task With_a_door_screen_only_its_current_code_checks_in_at_the_door()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(room.Id);
        ItIs(-60);
        var key = await SetUpScreenAsync(room.Id);
        ItIs(1);

        await FluentActions.Awaiting(async () => await CheckInAtDoorAsync(room.Id, await SecretOfAsync(room.Id)))
            .Should().ThrowAsync<ForbiddenAccessException>();
        await FluentActions.Awaiting(() => CheckInAtDoorAsync(room.Id, CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-19))))
            .Should().ThrowAsync<ForbiddenAccessException>("it opens the page, but is no proof of being there now");
        await FluentActions.Awaiting(() => CheckInAtDoorAsync(room.Id, CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(-40))))
            .Should().ThrowAsync<NotFoundException>("it doesn't even open the page any more");
        (await CheckInAtDoorAsync(room.Id, CheckInRules.ChangingCodeAt(key, Ten.AddMinutes(1)))).Id.Should().Be(booking);
    }

    [Fact]
    public async Task Away_from_the_door_a_check_in_still_needs_an_account()
    {
        var room = await AddRoomAsync();
        await BookAsync(room.Id);
        ItIs(0);

        await FluentActions.Awaiting(async () => await CheckInAtDoorAsync(room.Id, await SecretOfAsync(room.Id), atTheDoor: false))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    // --- Reporting a problem at the door ---

    [Fact]
    public async Task A_problem_is_reported_at_the_door_without_an_account()
    {
        var room = await AddRoomAsync();

        var issue = await ReportAtDoorAsync(room.Id, await SecretOfAsync(room.Id));

        issue.ResourceName.Should().Be(room.Name);
        issue.ReportedByUserId.Should().BeNull();
        issue.ReportedByName.Should().BeNull();
        issue.Status.Should().Be(IssueStatus.Open);
        _issues.Verify(n => n.IssueReportedAsync(It.Is<IssueReportDto>(d => d.Id == issue.Id), It.IsAny<CancellationToken>()), Times.Once);
        await using var context = _database.CreateContext();
        (await context.IssueReports.SingleAsync()).ReportedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task A_wrong_code_reports_nothing_and_the_same_problem_is_not_reported_twice()
    {
        var room = await AddRoomAsync();
        var code = await SecretOfAsync(room.Id);

        await FluentActions.Awaiting(() => ReportAtDoorAsync(room.Id, CheckInRules.NewCode())).Should().ThrowAsync<NotFoundException>();
        await ReportAtDoorAsync(room.Id, code);
        (await FluentActions.Awaiting(() => ReportAtDoorAsync(room.Id, code)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.IssueAlreadyReported);

        await using var context = _database.CreateContext();
        (await context.IssueReports.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Resolving_a_problem_reported_without_an_account_tells_nobody()
    {
        var room = await AddRoomAsync();
        var issue = await ReportAtDoorAsync(room.Id, await SecretOfAsync(room.Id));

        await using (var context = _database.CreateContext())
        {
            await new UpdateIssueStatusCommandHandler(
                    context, Admin, new IssueReader(_names), _issues.Object,
                    TestInvitations.For(context, Admin, notifier: _inbox.Object, clock: _clock), _clock)
                .Handle(new UpdateIssueStatusCommand(IssueStatus.Resolved) { Id = issue.Id }, CancellationToken.None);
        }

        await using var read = _database.CreateContext();
        (await read.IssueReports.SingleAsync()).Status.Should().Be(IssueStatus.Resolved);
        (await read.InboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Away_from_the_door_a_report_still_needs_an_account()
    {
        var room = await AddRoomAsync();

        await FluentActions.Awaiting(() => ReportAtDoorAsync(room.Id, code: null)).Should().ThrowAsync<UnauthorizedException>();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("0123456789abcdef0123456789abcdef0", false)] // one longer than a printed code
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    public void A_door_code_has_a_codes_shape(string? code, bool valid)
    {
        var command = new CreateIssueReportCommand(Guid.NewGuid(), IssueCategory.Display, "The TV doesn't turn on", false, null)
        {
            DoorCode = code,
        };

        var errors = new CreateIssueReportCommandValidator().Validate(command).Errors;

        errors.Any(e => e.PropertyName == nameof(CreateIssueReportCommand.DoorCode)).Should().Be(!valid);
    }
}
