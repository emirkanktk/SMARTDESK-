using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;
using SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetExtensionOptions;
using SmartDesk.Application.Features.Reservations.Queries.GetSeriesPreview;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Waitlist.Commands.JoinWaitlist;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.BookingRules;

// Every way of booking keeps to a kind's rules (ADR-0046): booking, moving, a series, running
// longer, the waiting list; and so does everything that offers times.
public sealed class BookingPolicyTests : IDisposable
{
    // Monday 5 October 2026, 08:00. The office keeps to UTC here, so the times read as they are.
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Day.AddHours(8);

    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Admin = new(Guid.NewGuid(), Roles.Admin);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));

    public void Dispose() => _database.Dispose();

    private static DateTime At(int day, double hour) => Day.AddDays(day).AddHours(hour);

    // Desks' rules, as an Admin sets them.
    private async Task DeskRulesAsync(int? maxMinutes = null, int? daysAhead = null, int? perPerson = null, bool officeHours = false)
    {
        await using var context = _database.CreateContext();
        var office = await context.OfficeHours.SingleAsync();
        office.TimeZone = "UTC";
        var desks = await context.ResourceTypes.SingleAsync(t => t.Id == ResourceTypeSeedData.DeskId);
        desks.MaxDurationMinutes = maxMinutes;
        desks.MaxDaysAhead = daysAhead;
        desks.MaxActivePerUser = perPerson;
        desks.OfficeHoursOnly = officeHours;
        await context.SaveChangesAsync();
    }

    private Task<Resource> DeskAsync(string name = "Desk 1") =>
        _database.AddResourceAsync(name, ResourceTypeSeedData.DeskId, capacity: 1);

    private Task<Resource> RoomAsync() => _database.AddResourceAsync("Board Room");

    private async Task<ReservationDto> BookAsync(Guid resourceId, DateTime start, DateTime end, FakeCurrentUser? user = null)
    {
        var caller = user ?? Ayse;
        await using var context = _database.CreateContext();
        return await new CreateReservationCommandHandler(
                context, caller, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), TestInvitations.For(context, caller),
                TestPolicy.For(context, _clock), TestApprovals.For(context, _clock))
            .Handle(new CreateReservationCommand(resourceId, start, end, null), CancellationToken.None);
    }

    private async Task<ReservationDto> MoveAsync(Guid id, Guid resourceId, DateTime start, DateTime end, FakeCurrentUser? user = null)
    {
        var caller = user ?? Ayse;
        await using var context = _database.CreateContext();
        return await new UpdateReservationCommandHandler(
                context, caller, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), _clock,
                TestInvitations.For(context, caller, clock: _clock), TestPolicy.For(context, _clock), TestApprovals.For(context, _clock))
            .Handle(new UpdateReservationCommand(resourceId, start, end, null) { Id = id }, CancellationToken.None);
    }

    private async Task<ReservationSeriesResultDto> SeriesAsync(Guid resourceId, DateTime start, DateTime end, DateOnly until, bool skip = false)
    {
        await using var context = _database.CreateContext();
        return await new CreateReservationSeriesCommandHandler(
                context, Ayse, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), TestInvitations.For(context, Ayse), _clock,
                TestPolicy.For(context, _clock))
            .Handle(
                new CreateReservationSeriesCommand(
                    resourceId, start, end, null, new Recurrence(RecurrenceFrequency.Daily, 1, null, until, "UTC"), SkipConflicts: skip),
                CancellationToken.None);
    }

    private async Task<SeriesPreviewDto> PreviewAsync(Guid resourceId, DateTime start, DateTime end, DateOnly until)
    {
        await using var context = _database.CreateContext();
        return await new GetSeriesPreviewQueryHandler(context, Ayse, TestPolicy.For(context, _clock))
            .Handle(new GetSeriesPreviewQuery(resourceId, start, end, RecurrenceFrequency.Daily, until, "UTC"), CancellationToken.None);
    }

    private async Task<int> CountBookingsAsync(Guid resourceId)
    {
        await using var context = _database.CreateContext();
        return await context.Reservations.CountAsync(r => r.ResourceId == resourceId && r.Status == ReservationStatus.Confirmed);
    }

    private static async Task<ValidationException> RefusedAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ValidationException>()).Which;

    private static async Task<ConflictException> ConflictAsync(Func<Task> act) =>
        (await act.Should().ThrowAsync<ConflictException>()).Which;

    // --- Booking ---

    [Fact]
    public async Task ABookingTheKindsRulesDontAllow_IsA400_SayingWhich()
    {
        await DeskRulesAsync(maxMinutes: 240, daysAhead: 7, officeHours: true);
        var desk = await DeskAsync();

        (await RefusedAsync(() => BookAsync(desk.Id, At(0, 17), At(0, 19)))).Errors.Should().ContainSingle()
            .Which.Should().Match<FluentValidation.Results.ValidationFailure>(e =>
                e.PropertyName == "StartDateTime" && e.ErrorMessage == "Desk bookings must be within office hours: Mon-Fri 09:00-18:00.");
        (await RefusedAsync(() => BookAsync(desk.Id, At(1, 9), At(1, 14)))).Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Desk bookings can last at most 4 hours.");
        (await RefusedAsync(() => BookAsync(desk.Id, At(8, 9), At(8, 10)))).Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Desk bookings can start at most 7 days ahead.");

        (await CountBookingsAsync(desk.Id)).Should().Be(0);
        await BookAsync(desk.Id, At(7, 9), At(7, 13));
    }

    [Fact]
    public async Task AnotherKind_KeepsToItsOwnRules()
    {
        await DeskRulesAsync(officeHours: true);
        var room = await RoomAsync();

        await BookAsync(room.Id, At(0, 17), At(0, 21));

        (await CountBookingsAsync(room.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ThePerPersonLimit_CountsTheirBookingsOfTheKindThatHaventEnded()
    {
        await DeskRulesAsync(perPerson: 2);
        var desk1 = await DeskAsync("Desk 1");
        var desk2 = await DeskAsync("Desk 2");
        var room = await RoomAsync();

        // Ended, cancelled, another kind, someone else's: none of them count.
        await _database.AddReservationAsync(desk1.Id, At(-1, 9), At(-1, 10), userId: Ayse.UserId);
        await _database.AddReservationAsync(desk1.Id, At(3, 9), At(3, 10), ReservationStatus.Cancelled, Ayse.UserId);
        await _database.AddReservationAsync(room.Id, At(3, 9), At(3, 10), userId: Ayse.UserId);
        await _database.AddReservationAsync(desk2.Id, At(3, 9), At(3, 10), userId: Mehmet.UserId);

        // Under way: it counts until it ends.
        await _database.AddReservationAsync(desk1.Id, At(0, 7), At(0, 9), userId: Ayse.UserId);
        await BookAsync(desk2.Id, At(1, 9), At(1, 10));

        var third = await ConflictAsync(() => BookAsync(desk1.Id, At(2, 9), At(2, 10)));
        third.Code.Should().Be(ConflictCodes.BookingLimit);
        third.Message.Should().Be("Desk: at most 2 upcoming bookings per person. Cancel one first.");

        await BookAsync(desk1.Id, At(2, 9), At(2, 10), Mehmet);
    }

    // --- Moving ---

    [Fact]
    public async Task MovingABookingWithinItsKind_IsntOneMore_ButMovingOneOntoIt_Is()
    {
        await DeskRulesAsync(perPerson: 1);
        var desk1 = await DeskAsync("Desk 1");
        var desk2 = await DeskAsync("Desk 2");
        var room = await RoomAsync();
        var desk = await BookAsync(desk1.Id, At(1, 9), At(1, 10));
        var meeting = await BookAsync(room.Id, At(2, 9), At(2, 10));

        await MoveAsync(desk.Id, desk2.Id, At(2, 11), At(2, 12));

        (await ConflictAsync(() => MoveAsync(meeting.Id, desk1.Id, At(2, 9), At(2, 10)))).Code.Should().Be(ConflictCodes.BookingLimit);
    }

    [Fact]
    public async Task AnAdminMovingSomeonesBooking_CountsTheirs()
    {
        await DeskRulesAsync(perPerson: 1);
        var desk1 = await DeskAsync("Desk 1");
        var desk2 = await DeskAsync("Desk 2");
        var room = await RoomAsync();
        await BookAsync(desk1.Id, At(1, 9), At(1, 10));
        var meeting = await BookAsync(room.Id, At(2, 9), At(2, 10));

        (await ConflictAsync(() => MoveAsync(meeting.Id, desk2.Id, At(2, 9), At(2, 10), Admin))).Code.Should().Be(ConflictCodes.BookingLimit);
    }

    [Fact]
    public async Task RulesChanged_LeaveTheBookingsMadeBefore_WhichCanStillBeShortened()
    {
        var desk = await DeskAsync();
        await DeskRulesAsync();
        var late = await BookAsync(desk.Id, At(1, 17), At(1, 20));

        await DeskRulesAsync(officeHours: true);

        await MoveAsync(late.Id, desk.Id, At(1, 17), At(1, 19));
        (await RefusedAsync(() => MoveAsync(late.Id, desk.Id, At(2, 17), At(2, 19)))).Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("StartDateTime");
    }

    // --- A series ---

    [Fact]
    public async Task ASeries_SkipsTheDatesOutsideOfficeHours_AsDatesThatArentFree()
    {
        await DeskRulesAsync(officeHours: true);
        var desk = await DeskAsync();
        var friday = At(4, 9);
        var tuesday = DateOnly.FromDateTime(At(8, 0));

        var preview = await PreviewAsync(desk.Id, friday, friday.AddHours(1), tuesday);
        preview.Dates.Select(d => d.Free).Should().Equal(true, false, false, true, true);
        preview.Dates[1].Code.Should().Be(ConflictCodes.OutsideOfficeHours);
        preview.Dates[1].Reason.Should().Be("Outside office hours: Mon-Fri 09:00-18:00.");

        (await ConflictAsync(() => SeriesAsync(desk.Id, friday, friday.AddHours(1), tuesday))).Code.Should().Be(ConflictCodes.SeriesConflicts);

        var result = await SeriesAsync(desk.Id, friday, friday.AddHours(1), tuesday, skip: true);
        result.Booked.Select(r => r.StartDateTime).Should().Equal(friday, At(7, 9), At(8, 9));
        result.Skipped.Should().HaveCount(2).And.OnlyContain(d => d.Code == ConflictCodes.OutsideOfficeHours);
    }

    [Fact]
    public async Task ASeries_KeepsItsFirstDateAndItsReachToTheRules()
    {
        await DeskRulesAsync(daysAhead: 14, officeHours: true);
        var desk = await DeskAsync();

        (await RefusedAsync(() => SeriesAsync(desk.Id, At(0, 18), At(0, 19), DateOnly.FromDateTime(At(2, 0)))))
            .Errors.Should().ContainSingle().Which.PropertyName.Should().Be("StartDateTime");
        (await RefusedAsync(() => PreviewAsync(desk.Id, At(0, 9), At(0, 10), DateOnly.FromDateTime(At(20, 0)))))
            .Errors.Should().ContainSingle().Which.Should().Match<FluentValidation.Results.ValidationFailure>(e =>
                e.PropertyName == "Recurrence.Until"
                && e.ErrorMessage == "Desk bookings can start at most 14 days ahead: end the series sooner.");
    }

    [Fact]
    public async Task ASeries_CountsEveryDateItBooksAgainstThePerPersonLimit()
    {
        await DeskRulesAsync(perPerson: 3);
        var desk = await DeskAsync();

        var tooMany = await ConflictAsync(() => SeriesAsync(desk.Id, At(1, 9), At(1, 10), DateOnly.FromDateTime(At(4, 0))));

        tooMany.Code.Should().Be(ConflictCodes.BookingLimit);
        tooMany.Message.Should().Be("Desk: at most 3 upcoming bookings per person, and this series would make 4.");
        (await SeriesAsync(desk.Id, At(1, 9), At(1, 10), DateOnly.FromDateTime(At(3, 0)))).Booked.Should().HaveCount(3);
    }

    // --- Running longer ---

    [Fact]
    public async Task RunningLonger_StopsAtClosingTime()
    {
        await DeskRulesAsync(officeHours: true);
        var desk = await DeskAsync();
        var booking = await _database.AddReservationAsync(desk.Id, At(0, 16), At(0, 17.5), userId: Ayse.UserId);
        _clock.SetUtcNow(new DateTimeOffset(At(0, 17.3)));

        await using (var context = _database.CreateContext())
        {
            (await new GetExtensionOptionsQueryHandler(context, Ayse, _clock, TestPolicy.For(context, _clock))
                .Handle(new GetExtensionOptionsQuery(booking.Id), CancellationToken.None)).Minutes.Should().Equal(15, 30);
        }

        var tooLate = await ConflictAsync(() => ExtendAsync(booking.Id, 60));
        tooLate.Message.Should().Be("Desk bookings must be within office hours: Mon-Fri 09:00-18:00.");

        (await ExtendAsync(booking.Id, 30)).EndDateTime.Should().Be(At(0, 18));
    }

    private async Task<ReservationDto> ExtendAsync(Guid id, int minutes)
    {
        await using var context = _database.CreateContext();
        return await new ExtendReservationCommandHandler(
                context, Ayse, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), _clock,
                TestInvitations.For(context, Ayse, clock: _clock), TestPolicy.For(context, _clock))
            .Handle(new ExtendReservationCommand(minutes) { Id = id }, CancellationToken.None);
    }

    // --- What's offered ---

    [Fact]
    public async Task TheSearch_LeavesOutKindsWhoseRulesDontAllowTheTime()
    {
        await DeskRulesAsync(officeHours: true);
        await DeskAsync();
        await RoomAsync();

        (await SearchAsync(At(0, 18), At(0, 19))).Should().Equal("Board Room");
        (await SearchAsync(At(0, 10), At(0, 11))).Should().BeEquivalentTo("Board Room", "Desk 1");
    }

    private async Task<IReadOnlyList<string>> SearchAsync(DateTime start, DateTime end)
    {
        await using var context = _database.CreateContext();
        var found = await new GetAvailableResourcesQueryHandler(context, Ayse, TestAccess.Reader(), TestPolicy.For(context, _clock))
            .Handle(new GetAvailableResourcesQuery(start, end), CancellationToken.None);
        return found.Select(r => r.Name).ToList();
    }

    [Fact]
    public async Task TheTimesSuggestedInstead_KeepToOfficeHours()
    {
        await DeskRulesAsync(officeHours: true);
        var desk = await DeskAsync();
        await BookAsync(desk.Id, At(1, 16), At(1, 18), Mehmet);
        var command = new CreateReservationCommand(desk.Id, At(1, 17), At(1, 18), null);

        await using var context = _database.CreateContext();
        var policy = TestPolicy.For(context, _clock);
        var handler = new CreateReservationCommandHandler(
            context, Ayse, new FakeIdentityService(), Mock.Of<IReservationNotifier>(), TestInvitations.For(context, Ayse), policy, TestApprovals.For(context, _clock));
        var behavior = new BookingSuggestionsBehavior<CreateReservationCommand>(new BookingSuggester(context, Ayse, _clock, policy));
        var conflict = await ConflictAsync(() => behavior.Handle(command, ct => handler.Handle(command, ct), CancellationToken.None));

        var suggestions = conflict.Suggestions.Should().BeOfType<BookingSuggestions>().Subject;
        suggestions.Times.Should().Equal(
            new SuggestedTime(At(0, 17), At(0, 18)),
            new SuggestedTime(At(1, 15), At(1, 16)),
            new SuggestedTime(At(2, 9), At(2, 10)));
    }

    [Fact]
    public async Task TheWaitingList_TakesOnlyWhatCouldBeBooked()
    {
        await DeskRulesAsync(officeHours: true);
        var desk = await DeskAsync();
        await BookAsync(desk.Id, At(1, 17), At(1, 18), Mehmet);

        await using var context = _database.CreateContext();
        var act = () => new JoinWaitlistCommandHandler(context, Ayse, _clock, TestPolicy.For(context, _clock))
            .Handle(new JoinWaitlistCommand(desk.Id, At(1, 17), At(1, 19)), CancellationToken.None);

        (await RefusedAsync(act)).Errors.Should().ContainSingle().Which.PropertyName.Should().Be("StartDateTime");
    }
}
