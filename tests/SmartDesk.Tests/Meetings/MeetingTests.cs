using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;
using SmartDesk.Application.Features.Inbox.Commands.MarkAllRead;
using SmartDesk.Application.Features.Inbox.Commands.MarkRead;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Inbox.Queries.GetInbox;
using SmartDesk.Application.Features.Inbox.Queries.GetUnreadCount;
using SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Meetings.Queries.GetInvitees;
using SmartDesk.Application.Features.Meetings.Queries.GetMyInvitations;
using SmartDesk.Application.Features.Reservations.Commands.CancelReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Meetings;

// Inviting people to a booking and the messages that tell them (ADR-0032).
public sealed class MeetingTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ten = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    // Zeynep runs a team with Ayşe, Mehmet and (disabled) Deniz; Okan runs one with Selin.
    private static readonly Guid ZeynepId = Guid.NewGuid();
    private static readonly Guid OkanId = Guid.NewGuid();
    private static readonly UserAccount Zeynep = Person(ZeynepId, "Zeynep", "Kaya", Roles.Manager);
    private static readonly UserAccount Ayse = Person(Guid.NewGuid(), "Ayşe", "Yılmaz", Roles.Employee, ZeynepId);
    private static readonly UserAccount Mehmet = Person(Guid.NewGuid(), "Mehmet", "Demir", Roles.Employee, ZeynepId);
    private static readonly UserAccount Deniz = Person(Guid.NewGuid(), "Deniz", "Ak", Roles.Employee, ZeynepId, AccountStatus.Disabled);
    private static readonly UserAccount Okan = Person(OkanId, "Okan", "Şahin", Roles.Manager);
    private static readonly UserAccount Selin = Person(Guid.NewGuid(), "Selin", "Er", Roles.Employee, OkanId);
    private static readonly UserAccount Admin = Person(Guid.NewGuid(), "System", "Admin", Roles.Admin);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly Mock<IReservationNotifier> _reservations = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly List<UserAccount> _people = [Zeynep, Ayse, Mehmet, Deniz, Okan, Selin, Admin];
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly List<InboxMessageDto> _pushed = [];

    public MeetingTests()
    {
        _accounts
            .Setup(a => a.ListAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserFilter filter, CancellationToken _) => _people
                .Where(p => (filter.Status is null || p.Status == filter.Status) && (filter.ManagerId is null || p.ManagerId == filter.ManagerId))
                .ToList());
        _inbox
            .Setup(n => n.MessagesSentAsync(It.IsAny<IReadOnlyList<InboxMessageDto>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<InboxMessageDto> messages, CancellationToken _) => _pushed.AddRange(messages))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _database.Dispose();

    private static UserAccount Person(
        Guid id, string first, string last, string role, Guid? managerId = null, AccountStatus status = AccountStatus.Active) =>
        new(id, first, last, $"{first.ToLowerInvariant()}@company.example", role, status, managerId, null, Now, 0);

    private static FakeCurrentUser As(UserAccount person) =>
        new(person.Id, person.Role) { TeamId = person.Role == Roles.Manager ? person.Id : person.ManagerId };

    private FakeIdentityService Names() => new(_people.ToDictionary(p => p.Id, p => p.DisplayName));

    private MeetingInvitations Invitations(IApplicationDbContext context, FakeCurrentUser user) =>
        TestInvitations.For(context, user, _accounts.Object, _inbox.Object, _clock, Names());

    private async Task<Resource> AddRoomAsync(int capacity = 8, Guid? team = null, bool checkIn = false)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..10], capacity: capacity);
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

    private async Task<ReservationDto> BookAsync(
        UserAccount organizer, Guid roomId, IReadOnlyList<Guid>? attendees, string? note = "Weekly sync", DateTime? start = null)
    {
        var from = start ?? Ten;
        await using var context = _database.CreateContext();
        var user = As(organizer);
        return await new CreateReservationCommandHandler(context, user, Names(), _reservations.Object, Invitations(context, user))
            .Handle(new CreateReservationCommand(roomId, from, from.AddHours(1), note, null, attendees), CancellationToken.None);
    }

    private async Task<ReservationDto> UpdateAsync(
        UserAccount caller, ReservationDto booking, IReadOnlyList<Guid>? attendees, DateTime? start = null, string? note = "Weekly sync")
    {
        var from = start ?? booking.StartDateTime;
        await using var context = _database.CreateContext();
        var user = As(caller);
        return await new UpdateReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
            .Handle(new UpdateReservationCommand(booking.ResourceId, from, from.AddHours(1), note, null, attendees) { Id = booking.Id }, CancellationToken.None);
    }

    private async Task<List<InboxMessage>> MessagesAsync()
    {
        await using var context = _database.CreateContext();
        return await context.InboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync();
    }

    private static string? AttendeeError(ValidationException e) =>
        e.Errors.SingleOrDefault(error => error.PropertyName == "AttendeeIds")?.ErrorMessage;

    // --- Inviting ---

    [Fact]
    public async Task A_manager_invites_team_members_and_each_gets_a_message()
    {
        var room = await AddRoomAsync();

        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);

        booking.Attendees.Select(a => a.Name).Should().Equal("Ayşe Yılmaz", "Mehmet Demir");
        var messages = await MessagesAsync();
        messages.Select(m => m.RecipientId).Should().BeEquivalentTo([Ayse.Id, Mehmet.Id]);
        messages.Should().AllSatisfy(m =>
        {
            m.Kind.Should().Be(InboxMessageKind.MeetingInvitation);
            m.SenderId.Should().Be(ZeynepId);
            m.ReservationId.Should().Be(booking.Id);
            m.ResourceName.Should().Be(room.Name);
            m.StartDateTime.Should().Be(Ten);
            m.EndDateTime.Should().Be(Ten.AddHours(1));
            m.Note.Should().Be("Weekly sync");
        });
        _pushed.Should().HaveCount(2).And.AllSatisfy(m => m.SenderName.Should().Be("Zeynep Kaya"));
    }

    [Fact]
    public async Task An_employee_invites_nobody()
    {
        var room = await AddRoomAsync();

        var act = () => BookAsync(Ayse, room.Id, [Mehmet.Id]);

        AttendeeError((await act.Should().ThrowAsync<ValidationException>()).Which)
            .Should().Be("Only team managers and administrators can invite people.");
        (await MessagesAsync()).Should().BeEmpty();
        await using var context = _database.CreateContext();
        (await context.Reservations.CountAsync()).Should().Be(0, "nothing is booked when the list is refused");
    }

    [Theory]
    [InlineData("Selin")]
    [InlineData("Deniz")]
    public async Task A_manager_invites_only_their_own_teams_active_members(string who)
    {
        var room = await AddRoomAsync();
        var person = who == "Selin" ? Selin : Deniz;

        var act = () => BookAsync(Zeynep, room.Id, [person.Id]);

        AttendeeError((await act.Should().ThrowAsync<ValidationException>()).Which)
            .Should().Be("You can invite only your own team's active members.");
    }

    [Fact]
    public async Task An_admin_invites_anyone_who_may_use_the_room()
    {
        var zeynepsRoom = await AddRoomAsync(team: ZeynepId);

        var booking = await BookAsync(Admin, zeynepsRoom.Id, [Ayse.Id, Zeynep.Id]);
        booking.Attendees.Should().HaveCount(2);

        var act = () => BookAsync(Admin, zeynepsRoom.Id, [Selin.Id], start: Ten.AddHours(3));
        AttendeeError((await act.Should().ThrowAsync<ValidationException>()).Which)
            .Should().Be($"Selin Er can't use {zeynepsRoom.Name}: it belongs to another team.");
    }

    [Fact]
    public async Task Everyone_must_fit_in_the_room()
    {
        var room = await AddRoomAsync(capacity: 2);

        var act = () => BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);

        AttendeeError((await act.Should().ThrowAsync<ValidationException>()).Which)
            .Should().Be($"{room.Name} has room for 2; with you, 3 people would come.");
        (await BookAsync(Zeynep, room.Id, [Ayse.Id])).Attendees.Should().ContainSingle();
    }

    [Fact]
    public async Task The_organizer_isnt_on_their_own_list_and_nobody_is_on_it_twice()
    {
        var room = await AddRoomAsync();

        var booking = await BookAsync(Zeynep, room.Id, [ZeynepId, Ayse.Id, Ayse.Id]);

        booking.Attendees.Should().ContainSingle().Which.Id.Should().Be(Ayse.Id);
        (await MessagesAsync()).Should().ContainSingle();
    }

    [Fact]
    public void At_most_fifty_people_are_invited()
    {
        var tooMany = Enumerable.Range(0, MeetingRules.MaxAttendees + 1).Select(_ => Guid.NewGuid()).ToList();

        var create = new CreateReservationCommandValidator(_clock)
            .Validate(new CreateReservationCommand(Guid.NewGuid(), Ten, Ten.AddHours(1), null, null, tooMany));
        var update = new UpdateReservationCommandValidator()
            .Validate(new UpdateReservationCommand(Guid.NewGuid(), Ten, Ten.AddHours(1), null, null, tooMany));

        create.Errors.Should().ContainSingle(e => e.PropertyName == "AttendeeIds" && e.ErrorMessage == "At most 50 people can be invited.");
        update.Errors.Should().ContainSingle(e => e.PropertyName == "AttendeeIds");
    }

    // --- Changing and cancelling ---

    [Fact]
    public async Task A_new_list_invites_newcomers_and_tells_those_taken_off()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        _pushed.Clear();

        var changed = await UpdateAsync(Zeynep, booking, [Mehmet.Id]);

        changed.Attendees.Select(a => a.Id).Should().Equal(Mehmet.Id);
        var messages = (await MessagesAsync()).Skip(1).ToList();
        messages.Should().HaveCount(2);
        messages.Should().ContainSingle(m => m.RecipientId == Mehmet.Id && m.Kind == InboxMessageKind.MeetingInvitation);
        messages.Should().ContainSingle(m => m.RecipientId == Ayse.Id && m.Kind == InboxMessageKind.MeetingUninvited);
        _pushed.Should().HaveCount(2);
        _reservations.Verify(n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == booking.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_new_time_is_told_to_everyone_still_invited()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);

        await UpdateAsync(Zeynep, booking, [Ayse.Id, Mehmet.Id], start: Ten.AddHours(2));

        var changes = (await MessagesAsync()).Where(m => m.Kind == InboxMessageKind.MeetingChanged).ToList();
        changes.Select(m => m.RecipientId).Should().BeEquivalentTo([Ayse.Id, Mehmet.Id]);
        changes.Should().AllSatisfy(m => m.StartDateTime.Should().Be(Ten.AddHours(2)));
    }

    [Fact]
    public async Task A_new_note_alone_sends_nothing()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);

        await UpdateAsync(Zeynep, booking, [Ayse.Id], note: "Bring the numbers");

        (await MessagesAsync()).Should().ContainSingle("only the invitation");
    }

    [Fact]
    public async Task Someone_already_invited_stays_after_leaving_the_team()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        _people.Remove(Ayse);
        _people.Add(Ayse with { ManagerId = OkanId });

        var changed = await UpdateAsync(Zeynep, booking, [Ayse.Id], start: Ten.AddHours(1));

        changed.Attendees.Should().ContainSingle();
    }

    [Fact]
    public async Task An_admins_change_keeps_the_owner_as_the_organizer()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);

        var changed = await UpdateAsync(Admin, booking, [Ayse.Id, Okan.Id]);

        changed.UserId.Should().Be(ZeynepId);
        (await MessagesAsync()).Single(m => m.RecipientId == Okan.Id).SenderId.Should().Be(Admin.Id, "the message says who made the change");
    }

    [Fact]
    public async Task Cancelling_tells_everyone_invited()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);
        _pushed.Clear();

        await using (var context = _database.CreateContext())
        {
            var user = As(Zeynep);
            await new CancelReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
                .Handle(new CancelReservationCommand(booking.Id), CancellationToken.None);
        }

        var cancelled = (await MessagesAsync()).Where(m => m.Kind == InboxMessageKind.MeetingCancelled).ToList();
        cancelled.Select(m => m.RecipientId).Should().BeEquivalentTo([Ayse.Id, Mehmet.Id]);
        cancelled.Should().AllSatisfy(m => m.SenderId.Should().Be(ZeynepId));
        _pushed.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_release_tells_everyone_invited_from_nobody()
    {
        var room = await AddRoomAsync(checkIn: true);
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(20)));

        await using (var context = _database.CreateContext())
        {
            (await new ReleaseNoShowsCommandHandler(context, Names(), _reservations.Object, _clock, Invitations(context, new FakeCurrentUser(null)))
                .Handle(new ReleaseNoShowsCommand(), CancellationToken.None)).Should().Be(1);
        }

        var cancelled = (await MessagesAsync()).Last();
        cancelled.Kind.Should().Be(InboxMessageKind.MeetingCancelled);
        cancelled.RecipientId.Should().Be(Ayse.Id);
        cancelled.SenderId.Should().BeNull();
        booking.CheckInRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Whoever_of_the_meeting_gets_to_the_door_first_checks_it_in()
    {
        var room = await AddRoomAsync(checkIn: true);
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(-5)));
        string code;
        await using (var context = _database.CreateContext())
        {
            code = await context.Resources.Where(r => r.Id == room.Id).Select(r => r.CheckInCode).SingleAsync();
        }

        async Task<ReservationDto> CheckIn(UserAccount who)
        {
            await using var context = _database.CreateContext();
            return await new CheckInCommandHandler(context, As(who), Names(), _reservations.Object, _clock)
                .Handle(new CheckInCommand(code) { ResourceId = room.Id }, CancellationToken.None);
        }

        await FluentActions.Awaiting(() => CheckIn(Mehmet)).Should().ThrowAsync<ConflictException>("he isn't invited");
        (await CheckIn(Ayse)).Id.Should().Be(booking.Id);
    }

    // --- Lists ---

    [Fact]
    public async Task The_invited_list_has_upcoming_meetings_only()
    {
        var room = await AddRoomAsync();
        var upcoming = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        var later = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id], start: Ten.AddHours(4));
        var cancelled = await BookAsync(Zeynep, room.Id, [Ayse.Id], start: Ten.AddHours(8));
        await BookAsync(Zeynep, room.Id, [Mehmet.Id], start: Ten.AddHours(12));
        await using (var context = _database.CreateContext())
        {
            var reservation = await context.Reservations.SingleAsync(r => r.Id == cancelled.Id);
            reservation.Status = ReservationStatus.Cancelled;
            await context.SaveChangesAsync();
        }

        IReadOnlyList<ReservationDto> invited;
        await using (var context = _database.CreateContext())
        {
            invited = await new GetMyInvitationsQueryHandler(context, As(Ayse), Names(), _clock)
                .Handle(new GetMyInvitationsQuery(), CancellationToken.None);
        }

        invited.Select(r => r.Id).Should().Equal(upcoming.Id, later.Id);
        invited[0].UserDisplayName.Should().Be("Zeynep Kaya");

        _clock.SetUtcNow(new DateTimeOffset(Ten.AddHours(2)));
        await using (var context = _database.CreateContext())
        {
            (await new GetMyInvitationsQueryHandler(context, As(Ayse), Names(), _clock)
                .Handle(new GetMyInvitationsQuery(), CancellationToken.None)).Select(r => r.Id).Should().Equal(later.Id);
        }
    }

    [Fact]
    public async Task Invitees_are_a_managers_team_everyone_for_an_admin_and_nobody_for_an_employee()
    {
        async Task<IReadOnlyList<InviteeDto>> InviteesOf(UserAccount who) =>
            await new GetInviteesQueryHandler(new Invitees(_accounts.Object, As(who)))
                .Handle(new GetInviteesQuery(), CancellationToken.None);

        (await InviteesOf(Zeynep)).Select(p => p.Name).Should().Equal("Ayşe Yılmaz", "Mehmet Demir");
        (await InviteesOf(Admin)).Select(p => p.Id).Should().BeEquivalentTo([Zeynep.Id, Ayse.Id, Mehmet.Id, Okan.Id, Selin.Id]);
        (await InviteesOf(Ayse)).Should().BeEmpty();
    }

    // --- The inbox ---

    [Fact]
    public async Task The_inbox_is_the_readers_own_newest_first_with_who_sent_it()
    {
        var room = await AddRoomAsync();
        var first = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id], start: Ten.AddHours(3));

        IReadOnlyList<InboxMessageDto> inbox;
        await using (var context = _database.CreateContext())
        {
            inbox = await new GetInboxQueryHandler(context, As(Ayse), Names()).Handle(new GetInboxQuery(), CancellationToken.None);
        }

        inbox.Should().HaveCount(2);
        inbox.Should().AllSatisfy(m => m.RecipientId.Should().Be(Ayse.Id));
        inbox[0].StartDateTime.Should().Be(Ten.AddHours(3), "the newest comes first");
        inbox[1].ReservationId.Should().Be(first.Id);
        inbox.Should().AllSatisfy(m => m.SenderName.Should().Be("Zeynep Kaya"));
    }

    [Fact]
    public async Task Messages_are_marked_read_one_at_a_time_or_all_at_once_by_their_reader_only()
    {
        var room = await AddRoomAsync();
        await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);
        await BookAsync(Zeynep, room.Id, [Ayse.Id], start: Ten.AddHours(3));
        var aysesFirst = (await MessagesAsync()).First(m => m.RecipientId == Ayse.Id);
        var mehmets = (await MessagesAsync()).Single(m => m.RecipientId == Mehmet.Id);

        async Task<int> UnreadOf(UserAccount who)
        {
            await using var context = _database.CreateContext();
            return (await new GetUnreadInboxCountQueryHandler(context, As(who)).Handle(new GetUnreadInboxCountQuery(), CancellationToken.None)).Unread;
        }

        async Task MarkRead(UserAccount who, Guid id)
        {
            await using var context = _database.CreateContext();
            await new MarkInboxMessageReadCommandHandler(context, As(who), _clock).Handle(new MarkInboxMessageReadCommand(id), CancellationToken.None);
        }

        (await UnreadOf(Ayse)).Should().Be(2);
        await MarkRead(Ayse, aysesFirst.Id);
        await MarkRead(Ayse, aysesFirst.Id);
        (await UnreadOf(Ayse)).Should().Be(1);

        await FluentActions.Awaiting(() => MarkRead(Ayse, mehmets.Id)).Should().ThrowAsync<NotFoundException>();
        (await UnreadOf(Mehmet)).Should().Be(1, "someone else can't mark it");

        await using (var context = _database.CreateContext())
        {
            await new MarkAllInboxMessagesReadCommandHandler(context, As(Ayse), _clock)
                .Handle(new MarkAllInboxMessagesReadCommand(), CancellationToken.None);
        }

        (await UnreadOf(Ayse)).Should().Be(0);
        (await UnreadOf(Mehmet)).Should().Be(1);
        (await MessagesAsync()).Single(m => m.Id == aysesFirst.Id).ReadAt.Should().Be(Now);
    }

    // --- Answering ---

    private async Task<ReservationDto> RespondAsync(
        UserAccount who, ReservationDto booking, AttendeeResponse response, string? reason = null)
    {
        await using var context = _database.CreateContext();
        var user = As(who);
        return await new RespondToInvitationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
            .Handle(new RespondToInvitationCommand(response, reason) { Id = booking.Id }, CancellationToken.None);
    }

    private async Task<ReservationAttendee> AttendeeAsync(ReservationDto booking, UserAccount who)
    {
        await using var context = _database.CreateContext();
        return await context.ReservationAttendees.AsNoTracking()
            .SingleAsync(a => a.ReservationId == booking.Id && a.UserId == who.Id);
    }

    private static AttendeeResponse ResponseOf(ReservationDto booking, UserAccount who) =>
        booking.Attendees.Single(a => a.Id == who.Id).Response;

    [Fact]
    public async Task Accepting_tells_the_organizer_and_shows_on_the_list()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);
        _pushed.Clear();

        var answered = await RespondAsync(Ayse, booking, AttendeeResponse.Accepted);

        ResponseOf(answered, Ayse).Should().Be(AttendeeResponse.Accepted);
        ResponseOf(answered, Mehmet).Should().Be(AttendeeResponse.Pending);
        (await AttendeeAsync(booking, Ayse)).RespondedAt.Should().Be(Now);
        var message = (await MessagesAsync()).Single(m => m.RecipientId == ZeynepId);
        message.Kind.Should().Be(InboxMessageKind.MeetingAccepted);
        message.SenderId.Should().Be(Ayse.Id);
        message.Reason.Should().BeNull();
        message.ResourceName.Should().Be(room.Name);
        _pushed.Should().ContainSingle(m => m.RecipientId == ZeynepId && m.SenderName == "Ayşe Yılmaz");
        _reservations.Verify(
            n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == booking.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Declining_says_why_and_only_the_organizer_reads_it()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);

        var answered = await RespondAsync(Mehmet, booking, AttendeeResponse.Declined, "  At the dentist until 11  ");

        ResponseOf(answered, Mehmet).Should().Be(AttendeeResponse.Declined);
        (await AttendeeAsync(booking, Mehmet)).DeclineReason.Should().Be("At the dentist until 11");
        var message = (await MessagesAsync()).Single(m => m.RecipientId == ZeynepId);
        message.Kind.Should().Be(InboxMessageKind.MeetingDeclined);
        message.Reason.Should().Be("At the dentist until 11");
        (await MessagesAsync()).Where(m => m.RecipientId == Ayse.Id).Should().OnlyContain(
            m => m.Kind == InboxMessageKind.MeetingInvitation, "the others aren't told");
        _pushed.Last().Reason.Should().Be("At the dentist until 11");
    }

    [Fact]
    public async Task The_same_answer_again_tells_nobody_but_a_new_reason_does()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);

        await RespondAsync(Ayse, booking, AttendeeResponse.Accepted);
        await RespondAsync(Ayse, booking, AttendeeResponse.Accepted);
        (await MessagesAsync()).Count(m => m.RecipientId == ZeynepId).Should().Be(1);

        await RespondAsync(Ayse, booking, AttendeeResponse.Declined, "Sick");
        await RespondAsync(Ayse, booking, AttendeeResponse.Declined, " Sick ");
        await RespondAsync(Ayse, booking, AttendeeResponse.Declined, "Sick, back on Monday");

        var toZeynep = (await MessagesAsync()).Where(m => m.RecipientId == ZeynepId).ToList();
        toZeynep.Select(m => m.Kind).Should().Equal(
            InboxMessageKind.MeetingAccepted, InboxMessageKind.MeetingDeclined, InboxMessageKind.MeetingDeclined);
        toZeynep.Last().Reason.Should().Be("Sick, back on Monday");
        _reservations.Verify(
            n => n.ReservationUpdatedAsync(It.Is<ReservationDto>(r => r.Id == booking.Id), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Coming_after_all_forgets_the_reason()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        await RespondAsync(Ayse, booking, AttendeeResponse.Declined, "Sick");

        var answered = await RespondAsync(Ayse, booking, AttendeeResponse.Accepted, "ignored when coming");

        ResponseOf(answered, Ayse).Should().Be(AttendeeResponse.Accepted);
        (await AttendeeAsync(booking, Ayse)).DeclineReason.Should().BeNull();
        (await MessagesAsync()).Last().Reason.Should().BeNull();
    }

    [Theory]
    [InlineData("organizer")]
    [InlineData("outsider")]
    public async Task Only_someone_on_the_list_can_answer(string who)
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);

        var act = () => RespondAsync(who == "organizer" ? Zeynep : Selin, booking, AttendeeResponse.Accepted);

        await act.Should().ThrowAsync<ForbiddenAccessException>();
        (await MessagesAsync()).Should().ContainSingle("only the invitation");
    }

    [Fact]
    public async Task A_cancelled_or_finished_meeting_cant_be_answered()
    {
        var room = await AddRoomAsync();
        var cancelled = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        var finished = await BookAsync(Zeynep, room.Id, [Ayse.Id], start: Ten.AddHours(3));
        await using (var context = _database.CreateContext())
        {
            var user = As(Zeynep);
            await new CancelReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
                .Handle(new CancelReservationCommand(cancelled.Id), CancellationToken.None);
        }

        await FluentActions.Awaiting(() => RespondAsync(Ayse, cancelled, AttendeeResponse.Accepted))
            .Should().ThrowAsync<ConflictException>();

        _clock.SetUtcNow(new DateTimeOffset(Ten.AddHours(4))); // its end
        await FluentActions.Awaiting(() => RespondAsync(Ayse, finished, AttendeeResponse.Accepted))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task A_new_start_asks_everyone_to_answer_again_but_staying_longer_doesnt()
    {
        var room = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id, Mehmet.Id]);
        await RespondAsync(Ayse, booking, AttendeeResponse.Accepted);
        await RespondAsync(Mehmet, booking, AttendeeResponse.Declined, "Sick");

        ReservationDto longer;
        await using (var context = _database.CreateContext())
        {
            var user = As(Zeynep);
            longer = await new UpdateReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
                .Handle(
                    new UpdateReservationCommand(room.Id, Ten, Ten.AddHours(2), "Weekly sync", null, [Ayse.Id, Mehmet.Id]) { Id = booking.Id },
                    CancellationToken.None);
        }

        ResponseOf(longer, Ayse).Should().Be(AttendeeResponse.Accepted);
        ResponseOf(longer, Mehmet).Should().Be(AttendeeResponse.Declined);

        var moved = await UpdateAsync(Zeynep, booking, [Ayse.Id, Mehmet.Id], start: Ten.AddHours(4));

        moved.Attendees.Should().OnlyContain(a => a.Response == AttendeeResponse.Pending);
        var mehmet = await AttendeeAsync(booking, Mehmet);
        mehmet.DeclineReason.Should().BeNull();
        mehmet.RespondedAt.Should().BeNull();
    }

    [Fact]
    public async Task Another_room_asks_everyone_to_answer_again()
    {
        var room = await AddRoomAsync();
        var other = await AddRoomAsync();
        var booking = await BookAsync(Zeynep, room.Id, [Ayse.Id]);
        await RespondAsync(Ayse, booking, AttendeeResponse.Accepted);

        ReservationDto moved;
        await using (var context = _database.CreateContext())
        {
            var user = As(Zeynep);
            moved = await new UpdateReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
                .Handle(
                    new UpdateReservationCommand(other.Id, Ten, Ten.AddHours(1), "Weekly sync", null, [Ayse.Id]) { Id = booking.Id },
                    CancellationToken.None);
        }

        ResponseOf(moved, Ayse).Should().Be(AttendeeResponse.Pending);
    }

    [Theory]
    [InlineData(AttendeeResponse.Accepted, null, null)]
    [InlineData(AttendeeResponse.Declined, "Sick", null)]
    [InlineData(AttendeeResponse.Declined, null, "Reason")]
    [InlineData(AttendeeResponse.Declined, "   ", "Reason")]
    [InlineData(AttendeeResponse.Pending, null, "Response")]
    public void Not_coming_says_why_and_Pending_is_no_answer(AttendeeResponse response, string? reason, string? failing)
    {
        var result = new RespondToInvitationCommandValidator().Validate(new RespondToInvitationCommand(response, reason));

        result.Errors.Select(e => e.PropertyName).Should().Equal(failing is null ? [] : [failing]);
    }

    [Fact]
    public void A_reason_is_at_most_as_long_as_a_note()
    {
        var validator = new RespondToInvitationCommandValidator();

        validator.Validate(new RespondToInvitationCommand(AttendeeResponse.Declined, new string('x', MeetingRules.ReasonMaxLength)))
            .IsValid.Should().BeTrue();
        validator.Validate(new RespondToInvitationCommand(AttendeeResponse.Declined, new string('x', MeetingRules.ReasonMaxLength + 1)))
            .IsValid.Should().BeFalse();
    }
}
