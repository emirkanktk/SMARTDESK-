using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;
using SmartDesk.Application.Features.Reservations.Commands.SendEndingSoonNotices;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetExtensionOptions;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

// Running over (ADR-0033): asked shortly before the end whether it needs longer, and extended
// with a click when the resource is free for it.
public sealed class ExtendReservationTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten.AddMinutes(30)));
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly Mock<IInboxNotifier> _inbox = new();

    public void Dispose() => _database.Dispose();

    private void ItIs(double minutesFromTen) => _clock.SetUtcNow(new DateTimeOffset(Ten.AddMinutes(minutesFromTen)));

    // Ayşe's meeting, 10:00-11:00 unless said otherwise.
    private async Task<(Resource Room, Reservation Meeting)> MeetingAsync(
        double startMinutes = 0, double endMinutes = 60, ResourceStatus status = ResourceStatus.Active)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..10], status: status);
        var meeting = await _database.AddReservationAsync(
            room.Id, Ten.AddMinutes(startMinutes), Ten.AddMinutes(endMinutes), userId: Ayse.UserId);
        return (room, meeting);
    }

    private async Task<Reservation> ReadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<int> NoticesAsync()
    {
        await using var context = _database.CreateContext();
        return await new SendEndingSoonNoticesCommandHandler(context, new FakeIdentityService(), _notifier.Object, _clock)
            .Handle(new SendEndingSoonNoticesCommand(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<int>> OptionsAsync(Guid id, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return (await new GetExtensionOptionsQueryHandler(context, user ?? Ayse, _clock)
            .Handle(new GetExtensionOptionsQuery(id), CancellationToken.None)).Minutes;
    }

    private async Task<ReservationDto> ExtendAsync(Guid id, int minutes, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        var caller = user ?? Ayse;
        return await new ExtendReservationCommandHandler(
                context, caller, new FakeIdentityService(), _notifier.Object, _clock,
                TestInvitations.For(context, caller, notifier: _inbox.Object, clock: _clock))
            .Handle(new ExtendReservationCommand(minutes) { Id = id }, CancellationToken.None);
    }

    private void VerifyAsked(Guid id, Times times) =>
        _notifier.Verify(n => n.ReservationEndingSoonAsync(
            It.Is<ReservationDto>(r => r.Id == id), It.IsAny<CancellationToken>()), times);

    // --- The question ---

    [Fact]
    public async Task The_owner_is_asked_once_in_the_last_ten_minutes()
    {
        var (_, meeting) = await MeetingAsync();

        ItIs(49);
        (await NoticesAsync()).Should().Be(0, "it's more than ten minutes from the end");

        ItIs(50);
        (await NoticesAsync()).Should().Be(1);
        (await NoticesAsync()).Should().Be(0);
        VerifyAsked(meeting.Id, Times.Once());
        (await ReadAsync(meeting.Id)).EndingSoonSentAt.Should().Be(Ten.AddMinutes(50));
    }

    [Fact]
    public async Task Nobody_is_asked_about_a_booking_that_has_not_started_is_cancelled_or_is_over()
    {
        // Five minutes from 10:55: it ends within ten minutes, but hasn't started.
        var (_, later) = await MeetingAsync(startMinutes: 55, endMinutes: 60);
        var (_, cancelled) = await MeetingAsync();
        await using (var context = _database.CreateContext())
        {
            (await context.Reservations.SingleAsync(r => r.Id == cancelled.Id)).Status = ReservationStatus.Cancelled;
            await context.SaveChangesAsync();
        }

        var (_, over) = await MeetingAsync(endMinutes: 52);
        ItIs(52);

        (await NoticesAsync()).Should().Be(0);
        VerifyAsked(later.Id, Times.Never());
        VerifyAsked(cancelled.Id, Times.Never());
        VerifyAsked(over.Id, Times.Never());
    }

    [Fact]
    public async Task After_extending_the_owner_is_asked_again_before_the_new_end()
    {
        var (_, meeting) = await MeetingAsync();
        ItIs(52);
        await NoticesAsync();

        await ExtendAsync(meeting.Id, 30);
        (await ReadAsync(meeting.Id)).EndingSoonSentAt.Should().BeNull();

        ItIs(80);
        (await NoticesAsync()).Should().Be(1);
        VerifyAsked(meeting.Id, Times.Exactly(2));
    }

    [Fact]
    public async Task A_new_end_from_the_form_is_asked_about_again_too()
    {
        var (room, meeting) = await MeetingAsync();
        ItIs(52);
        await NoticesAsync();

        await using (var context = _database.CreateContext())
        {
            await new UpdateReservationCommandHandler(
                    context, Ayse, new FakeIdentityService(), _notifier.Object, _clock, TestInvitations.For(context, Ayse))
                .Handle(new UpdateReservationCommand(room.Id, Ten, Ten.AddMinutes(90), null) { Id = meeting.Id }, CancellationToken.None);
        }

        (await ReadAsync(meeting.Id)).EndingSoonSentAt.Should().BeNull();
    }

    // --- How much longer ---

    [Fact]
    public async Task Every_option_fits_when_the_room_is_free_after()
    {
        var (_, meeting) = await MeetingAsync();

        (await OptionsAsync(meeting.Id)).Should().Equal(15, 30, 60);
    }

    [Fact]
    public async Task Only_what_ends_before_the_next_booking_fits()
    {
        var (room, meeting) = await MeetingAsync();
        await _database.AddReservationAsync(room.Id, Ten.AddMinutes(80), Ten.AddMinutes(120));

        (await OptionsAsync(meeting.Id)).Should().Equal(15);
    }

    [Fact]
    public async Task Nothing_fits_when_the_next_booking_starts_at_the_end()
    {
        var (room, meeting) = await MeetingAsync();
        await _database.AddReservationAsync(room.Id, Ten.AddMinutes(60), Ten.AddMinutes(120));

        (await OptionsAsync(meeting.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_blocked_window_and_a_cancelled_booking_count_as_they_should()
    {
        var (room, meeting) = await MeetingAsync();
        await _database.AddReservationAsync(room.Id, Ten.AddMinutes(60), Ten.AddMinutes(90), ReservationStatus.Cancelled);
        await _database.AddBlockAsync(room.Id, Ten.AddMinutes(100), Ten.AddMinutes(200));

        (await OptionsAsync(meeting.Id)).Should().Equal(15, 30);
    }

    [Fact]
    public async Task A_resource_that_is_not_active_takes_no_more_time()
    {
        var (_, meeting) = await MeetingAsync(status: ResourceStatus.Maintenance);

        (await OptionsAsync(meeting.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_owner_or_an_admin_may_ask_and_only_while_it_is_on()
    {
        var (_, meeting) = await MeetingAsync();

        await FluentActions.Awaiting(() => OptionsAsync(meeting.Id, new FakeCurrentUser(Guid.NewGuid(), Roles.Employee)))
            .Should().ThrowAsync<ForbiddenAccessException>();
        (await OptionsAsync(meeting.Id, new FakeCurrentUser(Guid.NewGuid(), Roles.Admin))).Should().NotBeEmpty();

        ItIs(60);
        await FluentActions.Awaiting(() => OptionsAsync(meeting.Id)).Should().ThrowAsync<ConflictException>();
    }

    // --- Extending ---

    [Fact]
    public async Task Extending_moves_only_the_end_and_tells_everyone()
    {
        var (_, meeting) = await MeetingAsync();
        await using (var context = _database.CreateContext())
        {
            var tracked = await context.Reservations.SingleAsync(r => r.Id == meeting.Id);
            tracked.CheckInRequired = true;
            tracked.CheckedInAt = Ten.AddMinutes(2);
            tracked.ReminderSentAt = Ten.AddMinutes(-15);
            await context.SaveChangesAsync();
        }

        var extended = await ExtendAsync(meeting.Id, 30);

        extended.StartDateTime.Should().Be(Ten);
        extended.EndDateTime.Should().Be(Ten.AddMinutes(90));
        var stored = await ReadAsync(meeting.Id);
        stored.CheckedInAt.Should().Be(Ten.AddMinutes(2), "it's the same visit");
        stored.CheckInRequired.Should().BeTrue();
        stored.ReminderSentAt.Should().Be(Ten.AddMinutes(-15));
        _notifier.Verify(n => n.ReservationUpdatedAsync(
            It.Is<ReservationDto>(r => r.Id == meeting.Id && r.EndDateTime == Ten.AddMinutes(90)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Everyone_invited_hears_that_it_runs_longer()
    {
        var (_, meeting) = await MeetingAsync();
        var mehmet = Guid.NewGuid();
        await using (var context = _database.CreateContext())
        {
            context.ReservationAttendees.Add(new ReservationAttendee
            {
                ReservationId = meeting.Id, UserId = mehmet, Response = AttendeeResponse.Accepted,
            });
            await context.SaveChangesAsync();
        }

        var extended = await ExtendAsync(meeting.Id, 15);

        extended.Attendees.Single().Response.Should().Be(AttendeeResponse.Accepted, "a longer meeting isn't a new one");
        await using var read = _database.CreateContext();
        var message = await read.InboxMessages.SingleAsync();
        message.RecipientId.Should().Be(mehmet);
        message.Kind.Should().Be(InboxMessageKind.MeetingExtended);
        message.SenderId.Should().Be(Ayse.UserId);
        message.EndDateTime.Should().Be(Ten.AddMinutes(75));
        _inbox.Verify(n => n.MessagesSentAsync(
            It.Is<IReadOnlyList<InboxMessageDto>>(m => m.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_extra_time_must_be_free()
    {
        var (room, meeting) = await MeetingAsync();
        await _database.AddReservationAsync(room.Id, Ten.AddMinutes(80), Ten.AddMinutes(120));

        var act = () => ExtendAsync(meeting.Id, 30);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Message.Should().Be($"{room.Name} is reserved right after, so this can't run 30 minutes longer.");
        (await ReadAsync(meeting.Id)).EndDateTime.Should().Be(Ten.AddMinutes(60));
        (await ExtendAsync(meeting.Id, 15)).EndDateTime.Should().Be(Ten.AddMinutes(75));
    }

    [Fact]
    public async Task A_blocked_window_or_a_resource_out_of_use_refuses_it()
    {
        var (room, meeting) = await MeetingAsync();
        await _database.AddBlockAsync(room.Id, Ten.AddMinutes(70), Ten.AddMinutes(90), "Cleaning");
        var (_, inMaintenance) = await MeetingAsync(status: ResourceStatus.Maintenance);

        (await FluentActions.Awaiting(() => ExtendAsync(meeting.Id, 15)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.ResourceBlocked);
        (await FluentActions.Awaiting(() => ExtendAsync(inMaintenance.Id, 15)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.ResourceUnavailable);
    }

    [Fact]
    public async Task Only_the_owner_or_an_admin_extends_and_only_before_the_end()
    {
        var (_, meeting) = await MeetingAsync();

        await FluentActions.Awaiting(() => ExtendAsync(meeting.Id, 15, new FakeCurrentUser(Guid.NewGuid(), Roles.Employee)))
            .Should().ThrowAsync<ForbiddenAccessException>();
        (await ExtendAsync(meeting.Id, 15, new FakeCurrentUser(Guid.NewGuid(), Roles.Admin))).UserId.Should().Be(Ayse.UserId!.Value);

        ItIs(75);
        await FluentActions.Awaiting(() => ExtendAsync(meeting.Id, 15)).Should().ThrowAsync<ConflictException>();
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(45, false)]
    [InlineData(-15, false)]
    public void Only_the_offered_lengths_are_accepted(int minutes, bool valid)
    {
        new ExtendReservationCommandValidator().Validate(new ExtendReservationCommand(minutes)).IsValid.Should().Be(valid);
    }
}
