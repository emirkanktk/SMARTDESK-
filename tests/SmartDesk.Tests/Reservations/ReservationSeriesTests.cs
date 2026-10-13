using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Commands.CancelFollowing;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetSeriesPreview;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

// Bookings that repeat (ADR-0035): every date booked at once or not at all, the dates that
// aren't free skipped when asked, the rest of a series cancelled, and one message for all the
// dates of a meeting.
public sealed class ReservationSeriesTests : IDisposable
{
    // Monday 5 October 2026, 08:00 UTC.
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    // Mondays 12 October to 2 November 2026, 10:00-11:00 in Istanbul (07:00 UTC).
    private static readonly DateTime First = new(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Until = new(2026, 11, 2);

    private static readonly Guid ZeynepId = Guid.NewGuid();
    private static readonly UserAccount Zeynep = Person(ZeynepId, "Zeynep", "Kaya", Roles.Manager);
    private static readonly UserAccount Ayse = Person(Guid.NewGuid(), "Ayşe", "Yılmaz", Roles.Employee, ZeynepId);
    private static readonly UserAccount Mehmet = Person(Guid.NewGuid(), "Mehmet", "Demir", Roles.Employee, ZeynepId);
    private static readonly UserAccount Admin = Person(Guid.NewGuid(), "System", "Admin", Roles.Admin);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly Mock<IReservationNotifier> _reservations = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly List<UserAccount> _people = [Zeynep, Ayse, Mehmet, Admin];
    private readonly List<InboxMessageDto> _pushed = [];

    public ReservationSeriesTests()
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

    private static UserAccount Person(Guid id, string first, string last, string role, Guid? managerId = null) =>
        new(id, first, last, $"{first.ToLowerInvariant()}@company.example", role, AccountStatus.Active, managerId, null, Now, 0);

    private static FakeCurrentUser As(UserAccount person) =>
        new(person.Id, person.Role) { TeamId = person.Role == Roles.Manager ? person.Id : person.ManagerId };

    private FakeIdentityService Names() => new(_people.ToDictionary(p => p.Id, p => p.DisplayName));

    private MeetingInvitations Invitations(IApplicationDbContext context, FakeCurrentUser user) =>
        TestInvitations.For(context, user, _accounts.Object, _inbox.Object, _clock, Names());

    private static Recurrence Weekly(DateOnly? until = null, params DayOfWeek[] days) =>
        new(RecurrenceFrequency.Weekly, 1, days, until ?? Until, "Europe/Istanbul");

    private static CreateReservationSeriesCommand Command(
        Guid roomId, Recurrence? rule = null, bool skip = false, IReadOnlyList<Guid>? attendees = null, DateTime? first = null) =>
        new(roomId, first ?? First, (first ?? First).AddHours(1), "Weekly sync", rule ?? Weekly(), 15, attendees, skip);

    private async Task<ReservationSeriesResultDto> BookSeriesAsync(
        UserAccount organizer, Guid roomId, Recurrence? rule = null, bool skip = false, IReadOnlyList<Guid>? attendees = null)
    {
        await using var context = _database.CreateContext();
        var user = As(organizer);
        return await new CreateReservationSeriesCommandHandler(context, user, Names(), _reservations.Object, Invitations(context, user), _clock, TestPolicy.For(context, _clock))
            .Handle(Command(roomId, rule, skip, attendees), CancellationToken.None);
    }

    private async Task<Resource> AddRoomAsync(bool checkIn = false, ResourceStatus status = ResourceStatus.Active, Guid? team = null)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..10], status: status);
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

    private async Task<List<Reservation>> BookingsAsync(Guid roomId)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations
            .AsNoTracking()
            .Include(r => r.Attendees)
            .Where(r => r.ResourceId == roomId)
            .OrderBy(r => r.StartDateTime)
            .ToListAsync();
    }

    private async Task<List<InboxMessage>> MessagesAsync()
    {
        await using var context = _database.CreateContext();
        return await context.InboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync();
    }

    // --- Booking a series ---

    [Fact]
    public async Task Every_date_is_booked_with_the_note_the_reminder_and_the_check_in_of_the_first()
    {
        var room = await AddRoomAsync(checkIn: true);

        var result = await BookSeriesAsync(Zeynep, room.Id);

        result.Booked.Select(r => r.StartDateTime).Should().Equal(First, First.AddDays(7), First.AddDays(14), First.AddDays(21));
        result.Skipped.Should().BeEmpty();
        result.Booked.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(ReservationStatus.Confirmed);
            r.Note.Should().Be("Weekly sync");
            r.ReminderMinutes.Should().Be(15);
            r.CheckInRequired.Should().BeTrue();
            r.UserDisplayName.Should().Be("Zeynep Kaya");
            r.Series.Should().BeEquivalentTo(result.Series);
        });
        result.Series.Frequency.Should().Be(RecurrenceFrequency.Weekly);
        result.Series.DaysOfWeek.Should().Equal(DayOfWeek.Monday);
        result.Series.Until.Should().Be(Until);
        result.Series.TimeZone.Should().Be("Europe/Istanbul");

        var saved = await BookingsAsync(room.Id);
        saved.Should().HaveCount(4).And.OnlyContain(r => r.SeriesId == result.Series.Id && r.UserId == ZeynepId);
        _reservations.Verify(n => n.ReservationCreatedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task One_date_that_isnt_free_stops_the_whole_series()
    {
        var room = await AddRoomAsync();
        await _database.AddReservationAsync(room.Id, First.AddDays(14).AddMinutes(30), First.AddDays(14).AddHours(2));

        var act = () => BookSeriesAsync(Zeynep, room.Id);

        var conflict = (await act.Should().ThrowAsync<ConflictException>()).Which;
        conflict.Code.Should().Be(ConflictCodes.SeriesConflicts);
        conflict.Message.Should().Be($"1 of the 4 dates aren't free in {room.Name}. Look at the dates again, then book the others or choose another time.");
        (await BookingsAsync(room.Id)).Should().ContainSingle("nothing of the series was booked");
        await using var context = _database.CreateContext();
        (await context.ReservationSeries.CountAsync()).Should().Be(0);
        _reservations.Verify(n => n.ReservationCreatedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Asked_to_skip_them_it_books_the_free_dates_and_says_why_the_others_were_not()
    {
        var room = await AddRoomAsync();
        await _database.AddReservationAsync(room.Id, First.AddDays(7), First.AddDays(7).AddHours(1));
        await _database.AddBlockAsync(room.Id, First.AddDays(21).AddMinutes(-30), First.AddDays(21).AddMinutes(30), "Painting");

        var result = await BookSeriesAsync(Zeynep, room.Id, skip: true);

        result.Booked.Select(r => r.StartDateTime).Should().Equal(First, First.AddDays(14));
        result.Skipped.Should().Equal(
            new SeriesDateDto(First.AddDays(7), First.AddDays(7).AddHours(1), false, ConflictCodes.ReservationOverlaps,
                $"{room.Name} is already reserved for part of that time."),
            new SeriesDateDto(First.AddDays(21), First.AddDays(21).AddHours(1), false, ConflictCodes.ResourceBlocked,
                $"{room.Name} is blocked for part of that time: Painting"));
        (await BookingsAsync(room.Id)).Count(r => r.SeriesId == result.Series.Id).Should().Be(2);
    }

    [Fact]
    public async Task With_no_date_free_there_is_nothing_to_book_even_when_skipping()
    {
        var room = await AddRoomAsync();
        await _database.AddBlockAsync(room.Id, First.AddDays(-1), First.AddDays(30), "Renovation");

        var act = () => BookSeriesAsync(Zeynep, room.Id, skip: true);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Be($"None of the dates is free in {room.Name}.");
    }

    [Fact]
    public async Task A_resource_that_cant_be_booked_is_refused_as_for_one_booking()
    {
        var maintenance = await AddRoomAsync(status: ResourceStatus.Maintenance);
        var othersRoom = await AddRoomAsync(team: Guid.NewGuid());

        (await FluentActions.Awaiting(() => BookSeriesAsync(Zeynep, maintenance.Id)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.ResourceUnavailable);
        await FluentActions.Awaiting(() => BookSeriesAsync(Zeynep, othersRoom.Id)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Those_invited_come_to_every_date_and_get_one_message_for_the_series()
    {
        var room = await AddRoomAsync();

        var result = await BookSeriesAsync(Zeynep, room.Id, attendees: [Ayse.Id, Mehmet.Id]);

        result.Booked.Should().AllSatisfy(r => r.Attendees.Select(a => a.Name).Should().Equal("Ayşe Yılmaz", "Mehmet Demir"));
        var messages = await MessagesAsync();
        messages.Should().HaveCount(2).And.AllSatisfy(m =>
        {
            m.Kind.Should().Be(InboxMessageKind.SeriesInvitation);
            m.SeriesId.Should().Be(result.Series.Id);
            m.ReservationId.Should().Be(result.Booked[0].Id);
            m.StartDateTime.Should().Be(First);
            m.SenderId.Should().Be(ZeynepId);
        });
        messages.Select(m => m.RecipientId).Should().BeEquivalentTo([Ayse.Id, Mehmet.Id]);
        _pushed.Should().HaveCount(2).And.OnlyContain(m => m.Series != null && m.Series.Id == result.Series.Id && m.SenderName == "Zeynep Kaya");
    }

    [Fact]
    public async Task An_invitation_the_caller_may_not_make_stops_the_series()
    {
        var room = await AddRoomAsync();

        var act = () => BookSeriesAsync(Ayse, room.Id, attendees: [Mehmet.Id]);

        await act.Should().ThrowAsync<ValidationException>();
        (await BookingsAsync(room.Id)).Should().BeEmpty();
    }

    // --- Its rules ---

    private CreateReservationSeriesCommandValidator Validator() => new(_clock);

    private async Task<string?> ErrorAsync(CreateReservationSeriesCommand command, string property) =>
        (await Validator().ValidateAsync(command)).Errors.FirstOrDefault(e => e.PropertyName == property)?.ErrorMessage;

    [Fact]
    public async Task A_good_series_passes_the_rules()
    {
        (await Validator().ValidateAsync(Command(Guid.NewGuid()))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task The_first_date_follows_the_rules_of_any_booking()
    {
        (await ErrorAsync(Command(Guid.NewGuid(), first: Now.AddHours(-1)), "StartDateTime"))
            .Should().Be("A reservation can't start in the past.");
        (await ErrorAsync(Command(Guid.NewGuid()) with { EndDateTime = First }, "EndDateTime"))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task The_rule_is_checked_field_by_field_and_then_by_the_dates_it_gives()
    {
        var command = Command(Guid.NewGuid());

        (await ErrorAsync(command with { Recurrence = Weekly() with { Interval = 5 } }, "Recurrence.Interval"))
            .Should().Be("Repeat it every 1 to 4 days or weeks.");
        (await ErrorAsync(command with { Recurrence = Weekly() with { TimeZone = "Nowhere/Land" } }, "Recurrence.TimeZone"))
            .Should().Be("Unknown time zone. Use an IANA id such as 'Europe/Istanbul'.");
        (await ErrorAsync(command with { Recurrence = Weekly() with { DaysOfWeek = [(DayOfWeek)9] } }, "Recurrence.DaysOfWeek"))
            .Should().Be("Unknown day of the week.");
        (await ErrorAsync(command with { Recurrence = Weekly(null, DayOfWeek.Friday) }, "Recurrence.DaysOfWeek"))
            .Should().Be("The series has to include the day it starts on (Monday).");
        (await ErrorAsync(command with { Recurrence = Weekly(new DateOnly(2026, 10, 18)) }, "Recurrence.Until"))
            .Should().Be("That's only one date: pick a later end, or more days, for it to repeat.");
        (await ErrorAsync(command with { Recurrence = Weekly(new DateOnly(2027, 6, 1)) }, "Recurrence.Until"))
            .Should().Be("A series can run at most 180 days ahead: end it sooner.");
        (await Validator().ValidateAsync(command with { Recurrence = null! })).Errors
            .Should().Contain(e => e.PropertyName == "Recurrence");
    }

    // --- Seeing the dates first ---

    [Fact]
    public async Task The_preview_lists_every_date_and_which_are_free_and_books_nothing()
    {
        var room = await AddRoomAsync();
        await _database.AddReservationAsync(room.Id, First.AddDays(7), First.AddDays(7).AddHours(1));
        await using var context = _database.CreateContext();

        var preview = await new GetSeriesPreviewQueryHandler(context, As(Zeynep), TestPolicy.For(context, _clock)).Handle(
            new GetSeriesPreviewQuery(room.Id, First, First.AddHours(1), RecurrenceFrequency.Weekly, Until, "Europe/Istanbul"),
            CancellationToken.None);

        preview.Dates.Select(d => (d.StartDateTime, d.Free)).Should().Equal(
            (First, true), (First.AddDays(7), false), (First.AddDays(14), true), (First.AddDays(21), true));
        preview.Dates[1].Code.Should().Be(ConflictCodes.ReservationOverlaps);
        (await BookingsAsync(room.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task The_preview_has_the_rules_of_booking_a_series()
    {
        var validator = new GetSeriesPreviewQueryValidator(_clock);
        var query = new GetSeriesPreviewQuery(Guid.NewGuid(), First, First.AddHours(1), RecurrenceFrequency.Weekly, Until, "Europe/Istanbul");

        (await validator.ValidateAsync(query)).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(query with { Interval = 0 })).Errors.Should().Contain(e => e.PropertyName == "Recurrence.Interval");
        (await validator.ValidateAsync(query with { Until = new DateOnly(2026, 10, 12) })).Errors
            .Should().ContainSingle(e => e.PropertyName == "Recurrence.Until");
        (await validator.ValidateAsync(query with { DaysOfWeek = [DayOfWeek.Tuesday] })).Errors
            .Should().ContainSingle(e => e.PropertyName == "Recurrence.DaysOfWeek");
    }

    // --- Ending a series early ---

    private async Task<IReadOnlyList<ReservationDto>> CancelFollowingAsync(UserAccount caller, Guid id)
    {
        await using var context = _database.CreateContext();
        var user = As(caller);
        return await new CancelFollowingCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
            .Handle(new CancelFollowingCommand(id), CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_from_a_date_on_cancels_it_and_every_later_one_and_tells_those_invited_once()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id, attendees: [Ayse.Id]);
        _pushed.Clear();

        var cancelled = await CancelFollowingAsync(Zeynep, series.Booked[1].Id);

        cancelled.Select(r => r.Id).Should().Equal(series.Booked.Skip(1).Select(r => r.Id));
        cancelled.Should().OnlyContain(r => r.Status == ReservationStatus.Cancelled);
        (await BookingsAsync(room.Id)).Select(r => r.Status).Should().Equal(
            ReservationStatus.Confirmed, ReservationStatus.Cancelled, ReservationStatus.Cancelled, ReservationStatus.Cancelled);
        _reservations.Verify(n => n.ReservationCancelledAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        var message = (await MessagesAsync()).Single(m => m.Kind == InboxMessageKind.SeriesCancelled);
        message.RecipientId.Should().Be(Ayse.Id);
        message.StartDateTime.Should().Be(First.AddDays(7));
        message.SeriesId.Should().Be(series.Series.Id);
        _pushed.Should().ContainSingle(m => m.Kind == InboxMessageKind.SeriesCancelled);
    }

    [Fact]
    public async Task Dates_already_over_or_cancelled_stay_as_they_are_and_cancelling_again_does_nothing()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id);
        await using (var context = _database.CreateContext())
        {
            (await context.Reservations.SingleAsync(r => r.Id == series.Booked[2].Id)).Status = ReservationStatus.Cancelled;
            await context.SaveChangesAsync();
        }

        var cancelled = await CancelFollowingAsync(Zeynep, series.Booked[1].Id);
        var again = await CancelFollowingAsync(Zeynep, series.Booked[1].Id);

        cancelled.Select(r => r.Id).Should().Equal(series.Booked[1].Id, series.Booked[3].Id);
        again.Should().BeEmpty();
        _reservations.Verify(n => n.ReservationCancelledAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Only_the_owner_or_an_admin_ends_a_series_and_only_a_series_from_a_date_not_over()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id);
        var single = await _database.AddReservationAsync(room.Id, First.AddDays(1), First.AddDays(1).AddHours(1), userId: ZeynepId);

        await FluentActions.Awaiting(() => CancelFollowingAsync(Ayse, series.Booked[0].Id)).Should().ThrowAsync<ForbiddenAccessException>();
        (await FluentActions.Awaiting(() => CancelFollowingAsync(Zeynep, single.Id)).Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Be("This booking doesn't repeat.");
        await FluentActions.Awaiting(() => CancelFollowingAsync(Zeynep, Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();

        _clock.SetUtcNow(new DateTimeOffset(First.AddHours(2)));
        (await FluentActions.Awaiting(() => CancelFollowingAsync(Zeynep, series.Booked[0].Id)).Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Be("This reservation has already ended and can't be cancelled.");

        (await CancelFollowingAsync(Admin, series.Booked[1].Id)).Should().HaveCount(3, "an Admin manages reservations");
    }

    // --- Answering for all the dates ---

    private async Task<ReservationDto> RespondAsync(UserAccount caller, Guid id, AttendeeResponse response, string? reason = null, bool following = false)
    {
        await using var context = _database.CreateContext();
        var user = As(caller);
        return await new RespondToInvitationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user))
            .Handle(new RespondToInvitationCommand(response, reason, following) { Id = id }, CancellationToken.None);
    }

    private async Task<List<(Guid Reservation, AttendeeResponse Response, string? Reason)>> AnswersAsync(Guid userId)
    {
        await using var context = _database.CreateContext();
        return (await context.ReservationAttendees
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Reservation.StartDateTime)
                .Select(a => new { a.ReservationId, a.Response, a.DeclineReason })
                .ToListAsync())
            .Select(a => (a.ReservationId, a.Response, a.DeclineReason))
            .ToList();
    }

    [Fact]
    public async Task An_answer_for_the_following_dates_covers_each_and_tells_the_organizer_once()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id, attendees: [Ayse.Id]);
        _pushed.Clear();

        var answered = await RespondAsync(Ayse, series.Booked[1].Id, AttendeeResponse.Declined, " Training on Mondays ", following: true);

        answered.Id.Should().Be(series.Booked[1].Id);
        (await AnswersAsync(Ayse.Id)).Should().Equal(
            (series.Booked[0].Id, AttendeeResponse.Pending, null),
            (series.Booked[1].Id, AttendeeResponse.Declined, "Training on Mondays"),
            (series.Booked[2].Id, AttendeeResponse.Declined, "Training on Mondays"),
            (series.Booked[3].Id, AttendeeResponse.Declined, "Training on Mondays"));
        var message = (await MessagesAsync()).Single(m => m.RecipientId == ZeynepId);
        message.Kind.Should().Be(InboxMessageKind.SeriesDeclined);
        message.Reason.Should().Be("Training on Mondays");
        message.SeriesId.Should().Be(series.Series.Id);
        message.StartDateTime.Should().Be(First.AddDays(7));
        _reservations.Verify(n => n.ReservationUpdatedAsync(It.IsAny<ReservationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        // The same answer again changes nothing and tells nobody.
        await RespondAsync(Ayse, series.Booked[1].Id, AttendeeResponse.Declined, "Training on Mondays", following: true);
        (await MessagesAsync()).Count(m => m.RecipientId == ZeynepId).Should().Be(1);
    }

    [Fact]
    public async Task Without_following_an_answer_is_for_that_date_alone()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id, attendees: [Ayse.Id]);

        await RespondAsync(Ayse, series.Booked[2].Id, AttendeeResponse.Accepted);

        (await AnswersAsync(Ayse.Id)).Select(a => a.Response).Should().Equal(
            AttendeeResponse.Pending, AttendeeResponse.Pending, AttendeeResponse.Accepted, AttendeeResponse.Pending);
        (await MessagesAsync()).Single(m => m.RecipientId == ZeynepId).Kind.Should().Be(InboxMessageKind.MeetingAccepted);
    }

    [Fact]
    public async Task Following_skips_the_dates_they_were_taken_off_cancelled_or_that_are_over()
    {
        var room = await AddRoomAsync();
        var series = await BookSeriesAsync(Zeynep, room.Id, attendees: [Ayse.Id, Mehmet.Id]);
        // Ayşe taken off the third date; the fourth cancelled.
        await using (var context = _database.CreateContext())
        {
            var user = As(Zeynep);
            var third = series.Booked[2];
            await new UpdateReservationCommandHandler(context, user, Names(), _reservations.Object, _clock, Invitations(context, user), TestPolicy.For(context, _clock))
                .Handle(new UpdateReservationCommand(room.Id, third.StartDateTime, third.EndDateTime, third.Note, 15, [Mehmet.Id]) { Id = third.Id }, CancellationToken.None);
        }

        await CancelFollowingAsync(Zeynep, series.Booked[3].Id);
        _clock.SetUtcNow(new DateTimeOffset(First.AddHours(2))); // the first is over

        await RespondAsync(Ayse, series.Booked[1].Id, AttendeeResponse.Accepted, following: true);

        (await AnswersAsync(Ayse.Id)).Should().Equal(
            (series.Booked[0].Id, AttendeeResponse.Pending, null),
            (series.Booked[1].Id, AttendeeResponse.Accepted, null),
            (series.Booked[3].Id, AttendeeResponse.Pending, null));
    }
}
