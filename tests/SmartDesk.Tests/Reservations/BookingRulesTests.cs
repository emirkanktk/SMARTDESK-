using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reservations;

// The two booking rules decided on issue #41: a resource with bookings still to come can't go
// out of service, and nobody books more than 180 days ahead.
public sealed class BookingRulesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public void Dispose() => _database.Dispose();

    private static DateTime In(TimeSpan offset) => Now.UtcDateTime + offset;

    private async Task SwitchAsync(Guid resourceId, ResourceStatus status)
    {
        await using var context = _database.CreateContext();
        await new ChangeResourceStatusCommandHandler(context, TestAccess.Admin, TestAccess.Reader(), _clock)
            .Handle(new ChangeResourceStatusCommand(status) { Id = resourceId }, CancellationToken.None);
    }

    private async Task<ResourceStatus> StatusOfAsync(Guid resourceId)
    {
        await using var context = _database.CreateContext();
        return (await context.Resources.SingleAsync(r => r.Id == resourceId)).Status;
    }

    [Theory]
    [InlineData(ResourceStatus.Maintenance)]
    [InlineData(ResourceStatus.Inactive)]
    public async Task A_resource_with_bookings_to_come_stays_in_service(ResourceStatus status)
    {
        var camera = await _database.AddResourceAsync("Camera C-1");
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromDays(2)), In(TimeSpan.FromDays(2.1)));
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromDays(9)), In(TimeSpan.FromDays(9.1)));

        var act = () => SwitchAsync(camera.Id, status);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.ResourceHasReservations
                && e.Message == "2 confirmed reservation(s) on Camera C-1 from now on. Cancel or move them first.");
        (await StatusOfAsync(camera.Id)).Should().Be(ResourceStatus.Active);
    }

    [Fact]
    public async Task Past_running_and_cancelled_bookings_dont_hold_it()
    {
        var camera = await _database.AddResourceAsync("Camera C-1");
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromDays(-3)), In(TimeSpan.FromDays(-2.9)));
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromHours(-1)), In(TimeSpan.FromHours(1)));
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromDays(4)), In(TimeSpan.FromDays(4.1)), ReservationStatus.Cancelled);

        await SwitchAsync(camera.Id, ResourceStatus.Maintenance);

        (await StatusOfAsync(camera.Id)).Should().Be(ResourceStatus.Maintenance);
    }

    [Fact]
    public async Task Back_into_service_is_always_allowed()
    {
        var camera = await _database.AddResourceAsync("Camera C-1", status: ResourceStatus.Maintenance);
        await _database.AddReservationAsync(camera.Id, In(TimeSpan.FromDays(2)), In(TimeSpan.FromDays(2.1)));

        await SwitchAsync(camera.Id, ResourceStatus.Active);

        (await StatusOfAsync(camera.Id)).Should().Be(ResourceStatus.Active);
    }

    [Fact]
    public async Task The_edit_form_follows_the_same_rule_but_other_edits_go_through()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, In(TimeSpan.FromDays(2)), In(TimeSpan.FromDays(2.1)));

        Task Edit(ResourceStatus status)
        {
            var context = _database.CreateContext();
            return new UpdateResourceCommandHandler(context, TestAccess.Admin, TestAccess.Reader(), _clock)
                .Handle(new UpdateResourceCommand("Meeting Room A+", room.ResourceTypeId, 10, "2nd Floor", null, null, status) { Id = room.Id }, CancellationToken.None);
        }

        (await FluentActions.Awaiting(() => Edit(ResourceStatus.Inactive)).Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.ResourceHasReservations);
        await Edit(ResourceStatus.Active);

        await using var check = _database.CreateContext();
        var saved = await check.Resources.SingleAsync(r => r.Id == room.Id);
        (saved.Name, saved.Capacity, saved.Status).Should().Be(("Meeting Room A+", 10, ResourceStatus.Active));
    }

    [Fact]
    public void A_booking_starts_at_most_180_days_ahead()
    {
        var validator = new CreateReservationCommandValidator(_clock);
        var resource = Guid.NewGuid();

        var edge = validator.Validate(new CreateReservationCommand(resource, In(TimeSpan.FromDays(180)), In(TimeSpan.FromDays(180.1)), null));
        var beyond = validator.Validate(new CreateReservationCommand(resource, In(TimeSpan.FromDays(180) + TimeSpan.FromMinutes(1)), In(TimeSpan.FromDays(181)), null));

        edge.IsValid.Should().BeTrue();
        beyond.Errors.Should().ContainSingle(e =>
            e.PropertyName == "StartDateTime" && e.ErrorMessage == "A reservation can start at most 180 days ahead.");
    }

    [Fact]
    public async Task Moving_a_booking_keeps_to_the_same_horizon()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var booking = await _database.AddReservationAsync(room.Id, In(TimeSpan.FromDays(2)), In(TimeSpan.FromDays(2.1)), userId: OwnerId);

        async Task MoveAsync(TimeSpan to)
        {
            await using var context = _database.CreateContext();
            await new UpdateReservationCommandHandler(context, new FakeCurrentUser(OwnerId), new FakeIdentityService(), Mock.Of<IReservationNotifier>(), _clock, TestInvitations.For(context, new FakeCurrentUser(OwnerId)), TestPolicy.For(context, _clock), TestApprovals.For(context, _clock))
                .Handle(new UpdateReservationCommand(room.Id, In(to), In(to + TimeSpan.FromHours(1)), null) { Id = booking.Id }, CancellationToken.None);
        }

        var tooFar = () => MoveAsync(TimeSpan.FromDays(181));
        (await tooFar.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "StartDateTime");

        await MoveAsync(TimeSpan.FromDays(179));
    }

    [Fact]
    public void Searching_keeps_to_the_same_horizon()
    {
        var validator = new GetAvailableResourcesQueryValidator(_clock);

        validator.Validate(new GetAvailableResourcesQuery(In(TimeSpan.FromDays(179)), In(TimeSpan.FromDays(179.5)))).IsValid.Should().BeTrue();
        validator.Validate(new GetAvailableResourcesQuery(In(TimeSpan.FromDays(181)), In(TimeSpan.FromDays(181.5)))).Errors
            .Should().ContainSingle(e => e.PropertyName == "StartDateTime" && e.ErrorMessage == "The search can start at most 180 days ahead.");
    }
}
