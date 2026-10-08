using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;
using SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Issues.Queries.GetIssuePhoto;
using SmartDesk.Application.Features.Issues.Queries.GetIssueReports;
using SmartDesk.Application.Features.Issues.Queries.GetOpenIssues;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Issues;

// Problems reported at a resource, and those who look after it moving them on (ADR-0037).
public sealed class IssueReportTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);

    // Selin runs a team that Ayşe is in; Kerem runs another one; Mehmet is in no team.
    private static readonly Guid SelinId = Guid.NewGuid();
    private static readonly Guid KeremId = Guid.NewGuid();
    private static readonly FakeCurrentUser Selin = new(SelinId, Roles.Manager) { TeamId = SelinId };
    private static readonly FakeCurrentUser Kerem = new(KeremId, Roles.Manager) { TeamId = KeremId };
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee) { TeamId = SelinId };
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Admin = TestAccess.Admin;

    // A real 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IIssueNotifier> _notifier = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten));
    private readonly FakeIdentityService _names;

    public IssueReportTests()
    {
        _names = new FakeIdentityService(new Dictionary<Guid, string>
        {
            [Ayse.UserId!.Value] = "Ayşe Yılmaz",
            [Mehmet.UserId!.Value] = "Mehmet Demir",
        });
    }

    public void Dispose() => _database.Dispose();

    private async Task<Resource> AddRoomAsync(string name, Guid? team = null)
    {
        var room = await _database.AddResourceAsync(name);
        if (team is { } teamId)
        {
            await using var context = _database.CreateContext();
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = room.Id, TeamId = teamId });
            await context.SaveChangesAsync();
        }

        return room;
    }

    private async Task<IssueReportDto> ReportAsync(
        Guid resourceId,
        IssueCategory? category = IssueCategory.Display,
        FakeCurrentUser? user = null,
        bool blocking = false,
        byte[]? photo = null,
        string description = "The TV doesn't turn on")
    {
        await using var context = _database.CreateContext();
        return await new CreateIssueReportCommandHandler(context, user ?? Ayse, new IssueReader(_names), _notifier.Object, _clock)
            .Handle(new CreateIssueReportCommand(resourceId, category, description, blocking, photo), CancellationToken.None);
    }

    private async Task<IssueReportDto> MoveAsync(Guid issueId, IssueStatus status, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        var by = user ?? Selin;
        return await new UpdateIssueStatusCommandHandler(
                context, by, new IssueReader(_names), _notifier.Object,
                TestInvitations.For(context, by, notifier: _inbox.Object, clock: _clock), _clock)
            .Handle(new UpdateIssueStatusCommand(status) { Id = issueId }, CancellationToken.None);
    }

    private async Task<IReadOnlyList<IssueReportDto>> ListAsync(FakeCurrentUser user, IssueStatus? status = null, Guid? resourceId = null)
    {
        await using var context = _database.CreateContext();
        return await new GetIssueReportsQueryHandler(context, user, new IssueReader(_names))
            .Handle(new GetIssueReportsQuery(status, resourceId), CancellationToken.None);
    }

    private async Task<IReadOnlyList<IssueReportDto>> OpenOnAsync(Guid resourceId, FakeCurrentUser user)
    {
        await using var context = _database.CreateContext();
        return await new GetOpenIssuesQueryHandler(context, user, new IssueReader(_names))
            .Handle(new GetOpenIssuesQuery(resourceId), CancellationToken.None);
    }

    private async Task<IssuePhotoFile> PhotoAsync(Guid issueId, FakeCurrentUser user)
    {
        await using var context = _database.CreateContext();
        return await new GetIssuePhotoQueryHandler(context, user).Handle(new GetIssuePhotoQuery(issueId), CancellationToken.None);
    }

    private void Later(double minutes) => _clock.Advance(TimeSpan.FromMinutes(minutes));

    // --- Reporting ---

    [Fact]
    public async Task Anyone_who_may_use_a_resource_reports_a_problem_and_those_who_manage_it_hear_about_it()
    {
        var room = await AddRoomAsync("Selin's Room", SelinId);

        var issue = await ReportAsync(room.Id, IssueCategory.Display, blocking: true, description: "  The TV doesn't turn on  ");

        issue.ResourceId.Should().Be(room.Id);
        issue.ResourceName.Should().Be("Selin's Room");
        issue.Category.Should().Be(IssueCategory.Display);
        issue.Description.Should().Be("The TV doesn't turn on");
        issue.IsBlocking.Should().BeTrue();
        issue.Status.Should().Be(IssueStatus.Open);
        issue.HasPhoto.Should().BeFalse();
        issue.ReportedByUserId.Should().Be(Ayse.UserId!.Value);
        issue.ReportedByName.Should().Be("Ayşe Yılmaz");
        issue.CreatedAt.Should().Be(Ten);
        issue.ResolvedAt.Should().BeNull();
        _notifier.Verify(n => n.IssueReportedAsync(It.Is<IssueReportDto>(i => i.Id == issue.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_problem_with_a_shared_resource_can_be_reported_by_someone_in_no_team()
    {
        var board = await AddRoomAsync("Board Room");

        var issue = await ReportAsync(board.Id, IssueCategory.Cleaning, Mehmet);

        issue.ReportedByName.Should().Be("Mehmet Demir");
    }

    [Fact]
    public async Task Another_teams_resource_is_not_there_for_reporting()
    {
        var room = await AddRoomAsync("Kerem's Room", KeremId);

        var act = () => ReportAsync(room.Id, user: Ayse);

        await act.Should().ThrowAsync<NotFoundException>();
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_same_problem_is_reported_once_until_it_is_fixed()
    {
        var room = await AddRoomAsync("Board Room");
        var first = await ReportAsync(room.Id, IssueCategory.Display);

        var again = () => ReportAsync(room.Id, IssueCategory.Display, Mehmet);

        (await again.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.IssueAlreadyReported
                && e.Message == "This problem with Board Room has already been reported and isn't fixed yet.");

        // Being worked on is still not fixed.
        await MoveAsync(first.Id, IssueStatus.InProgress, Admin);
        await again.Should().ThrowAsync<ConflictException>();

        // Another kind of problem is another report, and so is the same one once it's fixed.
        await ReportAsync(room.Id, IssueCategory.AirConditioning, Mehmet);
        await MoveAsync(first.Id, IssueStatus.Resolved, Admin);
        (await again()).Category.Should().Be(IssueCategory.Display);
    }

    [Fact]
    public async Task Other_can_be_reported_again_since_it_is_rarely_the_same_thing()
    {
        var room = await AddRoomAsync("Board Room");
        await ReportAsync(room.Id, IssueCategory.Other, description: "The door handle is loose");

        var second = await ReportAsync(room.Id, IssueCategory.Other, Mehmet, description: "The window won't close");

        second.Description.Should().Be("The window won't close");
    }

    [Fact]
    public async Task The_same_problem_on_another_resource_is_another_report()
    {
        var board = await AddRoomAsync("Board Room");
        var other = await AddRoomAsync("Meeting Room A");
        await ReportAsync(board.Id, IssueCategory.Projector);

        var act = () => ReportAsync(other.Id, IssueCategory.Projector);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_photo_is_kept_as_the_image_it_is_and_shown_to_those_who_may_use_the_resource()
    {
        var room = await AddRoomAsync("Selin's Room", SelinId);
        var issue = await ReportAsync(room.Id, photo: Png);

        issue.HasPhoto.Should().BeTrue();
        var photo = await PhotoAsync(issue.Id, Selin);
        photo.ContentType.Should().Be("image/png");
        photo.Data.Should().Equal(Png);
        (await PhotoAsync(issue.Id, Ayse)).Data.Should().Equal(Png);

        await FluentActions.Awaiting(() => PhotoAsync(issue.Id, Kerem)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task A_report_without_a_photo_has_none_to_show()
    {
        var room = await AddRoomAsync("Board Room");
        var issue = await ReportAsync(room.Id);

        await FluentActions.Awaiting(() => PhotoAsync(issue.Id, Admin)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void A_report_needs_a_category_and_a_description_and_any_photo_must_be_a_real_image()
    {
        var validator = new CreateIssueReportCommandValidator();
        var resourceId = Guid.NewGuid();

        validator.Validate(new CreateIssueReportCommand(resourceId, IssueCategory.Display, "Broken", false, null))
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateIssueReportCommand(resourceId, IssueCategory.Display, "Broken", true, Png))
            .IsValid.Should().BeTrue();

        var errors = validator.Validate(new CreateIssueReportCommand(resourceId, null, "   ", false, "<svg/>"u8.ToArray())).Errors;
        errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Category", "Description", "Photo"]);
        errors.Single(e => e.PropertyName == "Description").ErrorMessage.Should().Be("Describe the problem in a few words.");
        errors.Single(e => e.PropertyName == "Photo").ErrorMessage.Should().Be("The photo must be a JPEG, PNG or WebP image.");

        validator.Validate(new CreateIssueReportCommand(resourceId, (IssueCategory)42, new string('x', 501), false, new byte[3 * 1024 * 1024]))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Category", "Description", "Photo"]);
    }

    // --- Moving a report on ---

    [Fact]
    public async Task The_teams_manager_works_on_a_report_and_resolving_it_tells_whoever_reported_it()
    {
        var room = await AddRoomAsync("Selin's Room", SelinId);
        var issue = await ReportAsync(room.Id, IssueCategory.Projector, description: "No signal from HDMI");

        Later(5);
        var working = await MoveAsync(issue.Id, IssueStatus.InProgress);
        working.Status.Should().Be(IssueStatus.InProgress);
        working.ResolvedAt.Should().BeNull();
        _inbox.VerifyNoOtherCalls();

        Later(55);
        var resolved = await MoveAsync(issue.Id, IssueStatus.Resolved);
        resolved.Status.Should().Be(IssueStatus.Resolved);
        resolved.ResolvedAt.Should().Be(Ten.AddMinutes(60));
        _notifier.Verify(n => n.IssueUpdatedAsync(It.IsAny<IssueReportDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        await using var context = _database.CreateContext();
        var message = await context.InboxMessages.AsNoTracking().SingleAsync();
        message.RecipientId.Should().Be(Ayse.UserId!.Value);
        message.Kind.Should().Be(InboxMessageKind.IssueResolved);
        message.SenderId.Should().Be(SelinId);
        message.ResourceName.Should().Be("Selin's Room");
        message.IssueCategory.Should().Be(IssueCategory.Projector);
        message.Note.Should().Be("No signal from HDMI");
        message.StartDateTime.Should().Be(Ten);
        message.EndDateTime.Should().Be(Ten.AddMinutes(60));
        message.ReservationId.Should().BeNull();
        _inbox.Verify(
            n => n.MessagesSentAsync(
                It.Is<IReadOnlyList<InboxMessageDto>>(m => m.Count == 1 && m[0].IssueCategory == IssueCategory.Projector),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resolving_a_report_you_made_yourself_sends_no_message()
    {
        var room = await AddRoomAsync("Board Room");
        var issue = await ReportAsync(room.Id, user: Admin);

        await MoveAsync(issue.Id, IssueStatus.Resolved, Admin);

        await using var context = _database.CreateContext();
        (await context.InboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Resolved_is_final_and_resolving_again_changes_nothing()
    {
        var room = await AddRoomAsync("Board Room");
        var issue = await ReportAsync(room.Id);
        await MoveAsync(issue.Id, IssueStatus.Resolved, Admin);
        _notifier.Invocations.Clear();
        _inbox.Invocations.Clear();

        var reopen = () => MoveAsync(issue.Id, IssueStatus.Open, Admin);
        (await reopen.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.IssueResolved);

        Later(10);
        var again = await MoveAsync(issue.Id, IssueStatus.Resolved, Admin);
        again.ResolvedAt.Should().Be(Ten);
        _notifier.VerifyNoOtherCalls();
        _inbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Only_those_who_may_manage_the_resource_move_its_reports()
    {
        var teamRoom = await AddRoomAsync("Selin's Room", SelinId);
        var board = await AddRoomAsync("Board Room");
        var onTeamRoom = await ReportAsync(teamRoom.Id);
        var onBoard = await ReportAsync(board.Id);

        // Someone in the team uses the room but doesn't run it.
        await FluentActions.Awaiting(() => MoveAsync(onTeamRoom.Id, IssueStatus.InProgress, Ayse))
            .Should().ThrowAsync<ForbiddenAccessException>();
        // A shared resource is the Admins' to manage.
        await FluentActions.Awaiting(() => MoveAsync(onBoard.Id, IssueStatus.InProgress, Selin))
            .Should().ThrowAsync<ForbiddenAccessException>();
        // Another team's resource isn't there for Kerem.
        await FluentActions.Awaiting(() => MoveAsync(onTeamRoom.Id, IssueStatus.InProgress, Kerem))
            .Should().ThrowAsync<NotFoundException>();

        (await MoveAsync(onTeamRoom.Id, IssueStatus.InProgress, Selin)).Status.Should().Be(IssueStatus.InProgress);
        (await MoveAsync(onBoard.Id, IssueStatus.InProgress, Admin)).Status.Should().Be(IssueStatus.InProgress);
    }

    // --- Lists ---

    [Fact]
    public async Task Each_sees_the_reports_on_what_they_manage_unresolved_and_blocking_first()
    {
        var teamRoom = await AddRoomAsync("Selin's Room", SelinId);
        var board = await AddRoomAsync("Board Room");
        var keremsRoom = await AddRoomAsync("Kerem's Room", KeremId);

        var dirty = await ReportAsync(teamRoom.Id, IssueCategory.Cleaning);
        Later(1);
        var broken = await ReportAsync(teamRoom.Id, IssueCategory.Furniture);
        Later(1);
        var unusable = await ReportAsync(teamRoom.Id, IssueCategory.AirConditioning, blocking: true);
        Later(1);
        var fixedOne = await ReportAsync(teamRoom.Id, IssueCategory.Display, blocking: true);
        await MoveAsync(fixedOne.Id, IssueStatus.Resolved);
        var onBoard = await ReportAsync(board.Id, IssueCategory.Projector);
        var onKerems = await ReportAsync(keremsRoom.Id, IssueCategory.Projector, Kerem);

        (await ListAsync(Selin)).Select(i => i.Id).Should().Equal(unusable.Id, broken.Id, dirty.Id, fixedOne.Id);
        (await ListAsync(Kerem)).Select(i => i.Id).Should().Equal(onKerems.Id);
        (await ListAsync(Admin)).Should().HaveCount(6);
        (await ListAsync(Admin, IssueStatus.Resolved)).Select(i => i.Id).Should().Equal(fixedOne.Id);
        (await ListAsync(Admin, resourceId: board.Id)).Select(i => i.Id).Should().Equal(onBoard.Id);
        (await ListAsync(Ayse)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_page_at_the_door_shows_the_problems_still_open_there()
    {
        var teamRoom = await AddRoomAsync("Selin's Room", SelinId);
        var cleaning = await ReportAsync(teamRoom.Id, IssueCategory.Cleaning);
        Later(1);
        var noAir = await ReportAsync(teamRoom.Id, IssueCategory.AirConditioning, blocking: true);
        Later(1);
        var fixedOne = await ReportAsync(teamRoom.Id, IssueCategory.Display);
        await MoveAsync(fixedOne.Id, IssueStatus.Resolved);

        (await OpenOnAsync(teamRoom.Id, Ayse)).Select(i => i.Id).Should().Equal(noAir.Id, cleaning.Id);
        await FluentActions.Awaiting(() => OpenOnAsync(teamRoom.Id, Mehmet)).Should().ThrowAsync<NotFoundException>();
    }
}
