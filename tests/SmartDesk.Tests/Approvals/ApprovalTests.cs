using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Approvals.Commands.ApproveReservation;
using SmartDesk.Application.Features.Approvals.Commands.DeclineReservation;
using SmartDesk.Application.Features.Approvals.Commands.LapseUnapprovedRequests;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.Approvals.Queries.GetApprovalRequests;
using SmartDesk.Application.Features.BookingRules.Commands.UpdateResourceTypeRules;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;
using SmartDesk.Application.Features.Reservations.Commands.EndReservation;
using SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;
using SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Waitlist.Commands.JoinWaitlist;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Approvals;

// Bookings that wait for an Admin's approval (ADR-0049), as a company car's do here: asked for,
// holding their time, then approved, declined, or lapsed at their start.
public sealed class ApprovalTests : IDisposable
{
    // Monday 5 October 2026, 08:00 UTC.
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Day.AddHours(8);

    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);
    private static readonly FakeCurrentUser OtherAdmin = new(Guid.NewGuid(), Roles.Admin);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private readonly Mock<IInboxNotifier> _inbox = new();
    private readonly List<InboxMessageDto> _pushed = [];
    private readonly FakeIdentityService _names = new(new Dictionary<Guid, string>
    {
        [Ayse.UserId!.Value] = "Ayşe Yılmaz",
        [Mehmet.UserId!.Value] = "Mehmet Demir",
        [Admin.UserId!.Value] = "Selin Kaya",
        [OtherAdmin.UserId!.Value] = "Can Aydın",
    });

    public ApprovalTests()
    {
        _accounts
            .Setup(a => a.ListAsync(
                It.Is<UserFilter>(f => f.Role == Roles.Admin && f.Status == AccountStatus.Active), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Account(Admin), Account(OtherAdmin)]);
        _inbox
            .Setup(n => n.MessagesSentAsync(It.IsAny<IReadOnlyList<InboxMessageDto>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<InboxMessageDto> messages, CancellationToken _) => _pushed.AddRange(messages))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _database.Dispose();

    private static UserAccount Account(FakeCurrentUser user) =>
        new(user.UserId!.Value, "First", "Last", $"{user.UserId:N}@example.com", Roles.Admin, AccountStatus.Active, null, null, Now, 0);

    private static DateTime At(int day, double hour) => Day.AddDays(day).AddHours(hour);

    private async Task CarsNeedApprovalAsync()
    {
        await using var context = _database.CreateContext();
        (await context.ResourceTypes.SingleAsync(t => t.Id == ResourceTypeSeedData.CompanyCarId)).RequiresApproval = true;
        await context.SaveChangesAsync();
    }

    private Task<Resource> CarAsync(string name = "Company Car 1") =>
        _database.AddResourceAsync(name, ResourceTypeSeedData.CompanyCarId, capacity: 5);

    private MeetingInvitations Invitations(IApplicationDbContext context, ICurrentUserService user) =>
        TestInvitations.For(context, user, _accounts.Object, _inbox.Object, _clock, _names);

    private BookingApprovals Approvals(IApplicationDbContext context) =>
        new(context, _accounts.Object, Invitations(context, Admin), _clock);

    private async Task<ReservationDto> BookAsync(
        Guid resourceId, DateTime start, DateTime end, FakeCurrentUser? user = null, string? note = null, int? reminder = null)
    {
        var caller = user ?? Ayse;
        await using var context = _database.CreateContext();
        return await new CreateReservationCommandHandler(
                context, caller, _names, _notifier.Object, Invitations(context, caller), TestPolicy.For(context, _clock), Approvals(context))
            .Handle(new CreateReservationCommand(resourceId, start, end, note, reminder), CancellationToken.None);
    }

    private async Task<ReservationDto> MoveAsync(Guid id, Guid resourceId, DateTime start, DateTime end, FakeCurrentUser? user = null)
    {
        var caller = user ?? Ayse;
        await using var context = _database.CreateContext();
        return await new UpdateReservationCommandHandler(
                context, caller, _names, _notifier.Object, _clock, Invitations(context, caller), TestPolicy.For(context, _clock), Approvals(context))
            .Handle(new UpdateReservationCommand(resourceId, start, end, null) { Id = id }, CancellationToken.None);
    }

    private async Task<ReservationDto> ApproveAsync(Guid id, FakeCurrentUser? admin = null)
    {
        var who = admin ?? Admin;
        await using var context = _database.CreateContext();
        return await new ApproveReservationCommandHandler(context, who, _names, _notifier.Object, Invitations(context, who), Approvals(context), _clock)
            .Handle(new ApproveReservationCommand(id), CancellationToken.None);
    }

    private async Task<ReservationDto> DeclineAsync(Guid id, string reason)
    {
        await using var context = _database.CreateContext();
        return await new DeclineReservationCommandHandler(context, Admin, _names, _notifier.Object, Invitations(context, Admin), Approvals(context), _clock)
            .Handle(new DeclineReservationCommand(reason) { Id = id }, CancellationToken.None);
    }

    private async Task<int> LapseAsync()
    {
        await using var context = _database.CreateContext();
        return await new LapseUnapprovedRequestsCommandHandler(context, _names, _notifier.Object, Invitations(context, Admin), Approvals(context), _clock)
            .Handle(new LapseUnapprovedRequestsCommand(), CancellationToken.None);
    }

    private async Task<Reservation> ReadAsync(Guid id)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<List<InboxMessage>> InboxAsync()
    {
        await using var context = _database.CreateContext();
        return await context.InboxMessages.AsNoTracking().ToListAsync();
    }

    private static async Task<ConflictException> ConflictAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ConflictException>()).Which;

    private static async Task<ValidationFailure> RefusedAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle().Subject;

    [Fact]
    public async Task ACarBooking_IsAskedFor_HoldsItsTime_AndEveryAdminIsTold()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();

        var asked = await BookAsync(car.Id, At(1, 9), At(1, 17), note: "Client visit in Gebze");

        asked.Status.Should().Be(ReservationStatus.Pending);
        asked.RequestedAt.Should().Be(Now);
        (asked.DecidedAt, asked.DecidedBy, asked.DeclineReason).Should().Be(((DateTime?)null, (Guid?)null, (string?)null));
        var told = await InboxAsync();
        told.Should().HaveCount(2).And.OnlyContain(m =>
            m.Kind == InboxMessageKind.ApprovalRequested
            && m.SenderId == Ayse.UserId
            && m.ReservationId == asked.Id
            && m.Note == "Client visit in Gebze");
        told.Select(m => m.RecipientId).Should().BeEquivalentTo([Admin.UserId!.Value, OtherAdmin.UserId!.Value]);
        _pushed.Should().HaveCount(2).And.OnlyContain(m => m.SenderName == "Ayşe Yılmaz");

        // Nobody else has that time meanwhile.
        (await ConflictAsync(() => BookAsync(car.Id, At(1, 12), At(1, 13), Mehmet))).Code.Should().Be(ConflictCodes.ReservationOverlaps);

        // An Admin's own booking isn't asked for, and nobody is told.
        var adminsOwn = await BookAsync(car.Id, At(2, 9), At(2, 10), Admin);
        adminsOwn.Status.Should().Be(ReservationStatus.Confirmed);
        adminsOwn.RequestedAt.Should().BeNull();
        (await InboxAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ARequest_CantStartRightAway_ButOtherKindsCan()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var room = await _database.AddResourceAsync("Board Room");

        var refused = await RefusedAsync(() => BookAsync(car.Id, Now, Now.AddHours(2)));

        refused.PropertyName.Should().Be("StartDateTime");
        refused.ErrorMessage.Should().Be(
            "Company Car bookings wait for an Admin's approval, so they can't start right away: ask for a later time.");
        (await BookAsync(room.Id, Now, Now.AddHours(1))).Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task AnAdminApproves_ItsBooked_AndWhoeverAskedIsTold()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 17));
        _clock.Advance(TimeSpan.FromMinutes(30));

        var approved = await ApproveAsync(asked.Id);

        approved.Status.Should().Be(ReservationStatus.Confirmed);
        approved.RequestedAt.Should().Be(Now);
        approved.DecidedAt.Should().Be(Now.AddMinutes(30));
        (approved.DecidedBy, approved.DecidedByName).Should().Be((Admin.UserId, "Selin Kaya"));
        var told = (await InboxAsync()).Should().ContainSingle(m => m.Kind == InboxMessageKind.BookingApproved).Subject;
        (told.RecipientId, told.SenderId).Should().Be((Ayse.UserId!.Value, Admin.UserId));
        _notifier.Verify(
            n => n.ReservationUpdatedAsync(
                It.Is<ReservationDto>(r => r.Id == asked.Id && r.Status == ReservationStatus.Confirmed), It.IsAny<CancellationToken>()),
            Times.Once);

        (await ConflictAsync(() => ApproveAsync(asked.Id, OtherAdmin))).Code.Should().Be(ConflictCodes.NotAwaitingApproval);
        (await ConflictAsync(() => DeclineAsync(asked.Id, "Too late"))).Code.Should().Be(ConflictCodes.NotAwaitingApproval);
    }

    [Fact]
    public async Task AnAdminDeclines_SayingWhy_AndTheTimeIsFreeAgain()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 17));

        var declined = await DeclineAsync(asked.Id, "  The car is at the service that day.  ");

        declined.Status.Should().Be(ReservationStatus.Cancelled);
        declined.DeclineReason.Should().Be("The car is at the service that day.");
        declined.DecidedBy.Should().Be(Admin.UserId);
        var told = (await InboxAsync()).Should().ContainSingle(m => m.Kind == InboxMessageKind.BookingDeclined).Subject;
        (told.RecipientId, told.SenderId, told.Reason).Should().Be((Ayse.UserId!.Value, Admin.UserId, "The car is at the service that day."));
        _notifier.Verify(
            n => n.ReservationCancelledAsync(It.Is<ReservationDto>(r => r.Id == asked.Id), It.IsAny<CancellationToken>()), Times.Once);

        (await BookAsync(car.Id, At(1, 9), At(1, 17), Mehmet)).Status.Should().Be(ReservationStatus.Pending);
        new DeclineReservationCommandValidator().Validate(new DeclineReservationCommand(" ")).Errors
            .Should().ContainSingle().Which.ErrorMessage.Should().Be("Say why it's declined: whoever asked reads it.");
    }

    [Fact]
    public async Task ARequestStillWaitingAtItsStart_CantBeApproved_AndLapses()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(0, 9), At(0, 10));
        var later = await BookAsync(car.Id, At(1, 9), At(1, 10), Mehmet);
        _clock.SetUtcNow(new DateTimeOffset(At(0, 9)));

        (await ConflictAsync(() => ApproveAsync(asked.Id))).Code.Should().Be(ConflictCodes.ApprovalLapsed);
        (await LapseAsync()).Should().Be(1);
        (await LapseAsync()).Should().Be(0);

        var lapsed = await ReadAsync(asked.Id);
        lapsed.Status.Should().Be(ReservationStatus.Cancelled);
        (lapsed.DecidedAt, lapsed.DecidedByUserId).Should().Be((At(0, 9), (Guid?)null));
        var told = (await InboxAsync()).Should().ContainSingle(m => m.Kind == InboxMessageKind.ApprovalLapsed).Subject;
        (told.RecipientId, told.SenderId).Should().Be((Ayse.UserId!.Value, (Guid?)null));
        (await ReadAsync(later.Id)).Status.Should().Be(ReservationStatus.Pending, "its start hasn't come");
    }

    [Fact]
    public async Task AnOwnerMovingAnApprovedBooking_AsksAgain_ButShorteningIt_OrAnAdminsMove_DoesNot()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 17));
        await ApproveAsync(asked.Id);

        (await MoveAsync(asked.Id, car.Id, At(1, 9), At(1, 12))).Status.Should().Be(ReservationStatus.Confirmed);
        (await MoveAsync(asked.Id, car.Id, At(2, 9), At(2, 12), Admin)).Status.Should().Be(ReservationStatus.Confirmed);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var moved = await MoveAsync(asked.Id, car.Id, At(3, 9), At(3, 12));

        moved.Status.Should().Be(ReservationStatus.Pending);
        moved.RequestedAt.Should().Be(Now.AddMinutes(5));
        (moved.DecidedAt, moved.DecidedBy).Should().Be(((DateTime?)null, (Guid?)null));
        (await InboxAsync()).Count(m => m.Kind == InboxMessageKind.ApprovalRequested)
            .Should().Be(4, "both Admins heard when it was asked for, and again when it moved");
    }

    [Fact]
    public async Task ARequestMovedToAKindThatDoesntAsk_IsSimplyBooked()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var desk = await _database.AddResourceAsync("Desk 1", ResourceTypeSeedData.DeskId, capacity: 1);
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 10));

        var moved = await MoveAsync(asked.Id, desk.Id, At(1, 9), At(1, 10));

        moved.Status.Should().Be(ReservationStatus.Confirmed);
        moved.RequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task ARequest_CantRepeat_JoinAWaitingList_RunLonger_OrEnd()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 10));
        await using var context = _database.CreateContext();

        var series = await RefusedAsync(() => new CreateReservationSeriesCommandHandler(
                context, Ayse, _names, _notifier.Object, Invitations(context, Ayse), _clock, TestPolicy.For(context, _clock))
            .Handle(
                new CreateReservationSeriesCommand(
                    car.Id, At(2, 9), At(2, 10), null, new Recurrence(RecurrenceFrequency.Daily, 1, null, DateOnly.FromDateTime(At(5, 0)), "UTC")),
                CancellationToken.None));
        series.PropertyName.Should().Be("ResourceId");
        series.ErrorMessage.Should().Be(
            "Company Car bookings wait for an Admin's approval, so they can't repeat: ask for each date on its own.");

        var waiting = await ConflictAsync(() => new JoinWaitlistCommandHandler(context, Mehmet, _clock, TestPolicy.For(context, _clock))
            .Handle(new JoinWaitlistCommand(car.Id, At(1, 9), At(1, 10)), CancellationToken.None));
        waiting.Code.Should().Be(ConflictCodes.ApprovalRequired);

        var longer = await ConflictAsync(() => new ExtendReservationCommandHandler(
                context, Ayse, _names, _notifier.Object, _clock, Invitations(context, Ayse), TestPolicy.For(context, _clock))
            .Handle(new ExtendReservationCommand(30) { Id = asked.Id }, CancellationToken.None));
        longer.Code.Should().Be(ConflictCodes.AwaitingApproval);

        var ended = await ConflictAsync(() => new EndReservationCommandHandler(context, Ayse, _names, _notifier.Object, _clock)
            .Handle(new EndReservationCommand(asked.Id), CancellationToken.None));
        ended.Code.Should().Be(ConflictCodes.AwaitingApproval);
    }

    [Fact]
    public async Task WhatWaits_IsListedForAdmins_SoonestFirst_WithWhoAsked()
    {
        await CarsNeedApprovalAsync();
        var car1 = await CarAsync();
        var car2 = await CarAsync("Company Car 2");
        var later = await BookAsync(car1.Id, At(3, 9), At(3, 10));
        var sooner = await BookAsync(car2.Id, At(1, 9), At(1, 10), Mehmet);
        await BookAsync(car1.Id, At(4, 9), At(4, 10), Admin);

        await using var context = _database.CreateContext();
        var waiting = await new GetApprovalRequestsQueryHandler(context, _names)
            .Handle(new GetApprovalRequestsQuery(), CancellationToken.None);

        waiting.Select(r => r.Id).Should().Equal(sooner.Id, later.Id);
        waiting[0].UserDisplayName.Should().Be("Mehmet Demir");
    }

    [Fact]
    public async Task WhileItWaits_NoReminder_And_WhatWasNeverBooked_IsntInTheReport()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var reminded = await BookAsync(car.Id, At(1, 9), At(1, 10), reminder: 15);
        var declined = await BookAsync(car.Id, At(2, 9), At(2, 10));
        await DeclineAsync(declined.Id, "Not that day");
        var lapsing = await BookAsync(car.Id, At(0, 9), At(0, 10));

        _clock.SetUtcNow(new DateTimeOffset(At(0, 9)));
        await LapseAsync();
        _clock.SetUtcNow(new DateTimeOffset(At(1, 8.8)));
        await using (var context = _database.CreateContext())
        {
            (await new SendDueRemindersCommandHandler(context, _names, _notifier.Object, _clock)
                .Handle(new SendDueRemindersCommand(), CancellationToken.None)).Should().Be(0, "it isn't booked yet");
        }

        await ApproveAsync(reminded.Id);
        await using (var context = _database.CreateContext())
        {
            (await new SendDueRemindersCommandHandler(context, _names, _notifier.Object, _clock)
                .Handle(new SendDueRemindersCommand(), CancellationToken.None)).Should().Be(1);

            var report = await new GetUsageReportQueryHandler(context, _names)
                .Handle(new GetUsageReportQuery(DateOnly.FromDateTime(Day), DateOnly.FromDateTime(At(3, 0)), "UTC"), CancellationToken.None);
            var row = report.Resources.Single(r => r.ResourceId == car.Id);
            (row.Reservations, row.Cancelled).Should().Be((1, 0), "a declined or lapsed request was never a booking");
        }

        (await ReadAsync(lapsing.Id)).Status.Should().Be(ReservationStatus.Cancelled);
    }

    [Fact]
    public async Task AnAdminSays_WhichKinds_WaitForApproval()
    {
        await using var context = _database.CreateContext();

        var saved = await new UpdateResourceTypeRulesCommandHandler(context)
            .Handle(new UpdateResourceTypeRulesCommand(null, null, null, false, RequiresApproval: true) { Id = ResourceTypeSeedData.CompanyCarId }, CancellationToken.None);

        saved.RequiresApproval.Should().BeTrue();
        (await context.ResourceTypes.AsNoTracking().SingleAsync(t => t.Id == ResourceTypeSeedData.CompanyCarId)).RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public async Task InACalendarApp_ARequestIsTentative()
    {
        await CarsNeedApprovalAsync();
        var car = await CarAsync();
        var asked = await BookAsync(car.Id, At(1, 9), At(1, 10));
        var links = new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200" }));

        CalendarEntries.For(asked, Ayse.UserId!.Value, links, TestCheckInTimes.Default.Grace).Status
            .Should().Be(CalendarEntryStatus.Tentative);
    }
}
