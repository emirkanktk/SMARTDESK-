using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.ResourceBlocks;

public class ResourceBlockHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Day = Now.UtcDateTime.Date;
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(double hour) => Day.AddHours(hour);

    private async Task<ResourceBlockDto> BlockAsync(Guid resourceId, double startHour, double endHour, string reason = "Projector replacement")
    {
        await using var context = _database.CreateContext();
        return await new CreateResourceBlockCommandHandler(context, new FakeCurrentUser(AdminId, Roles.Admin))
            .Handle(new CreateResourceBlockCommand(resourceId, At(startHour), At(endHour), $"  {reason} "), CancellationToken.None);
    }

    [Fact]
    public async Task Create_FreeWindow_StoresBlockWithCreator()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");

        var dto = await BlockAsync(room.Id, 10, 14);

        dto.ResourceName.Should().Be("Meeting Room A");
        dto.Reason.Should().Be("Projector replacement");
        dto.CreatedByUserId.Should().Be(AdminId);
        await using var verify = _database.CreateContext();
        (await verify.ResourceBlocks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_OverlappingAnotherBlock_ThrowsConflict()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddBlockAsync(room.Id, At(10), At(12));

        var act = () => BlockAsync(room.Id, 11, 13);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already blocked*");
    }

    [Fact]
    public async Task Create_OverlappingConfirmedReservations_ThrowsConflictWithCount_AndSavesNothing()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11));
        await _database.AddReservationAsync(room.Id, At(12), At(13));
        await _database.AddReservationAsync(room.Id, At(11), At(12), ReservationStatus.Cancelled);

        var act = () => BlockAsync(room.Id, 9, 14);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("2 confirmed reservation(s) on Meeting Room A overlap this window. Cancel or move them first.");
        await using var verify = _database.CreateContext();
        (await verify.ResourceBlocks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_BackToBackWithReservation_Succeeds()
    {
        var room = await _database.AddResourceAsync();
        await _database.AddReservationAsync(room.Id, At(10), At(11));

        var dto = await BlockAsync(room.Id, 11, 12);

        dto.StartDateTime.Should().Be(At(11));
    }

    [Fact]
    public async Task Create_UnknownResource_ThrowsNotFound()
    {
        var act = () => BlockAsync(Guid.NewGuid(), 10, 11);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_RemovesBlock_UnknownIdIsNotFound()
    {
        var room = await _database.AddResourceAsync();
        var block = await _database.AddBlockAsync(room.Id, At(10), At(12));

        await using (var context = _database.CreateContext())
        {
            await new DeleteResourceBlockCommandHandler(context, TestAccess.Admin).Handle(new DeleteResourceBlockCommand(block.Id), CancellationToken.None);
        }

        await using var verify = _database.CreateContext();
        (await verify.ResourceBlocks.AnyAsync()).Should().BeFalse();
        var act = () => new DeleteResourceBlockCommandHandler(verify, TestAccess.Admin).Handle(new DeleteResourceBlockCommand(block.Id), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetInRange_ReturnsOverlappingBlocks_FilteredByResource()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        var roomB = await _database.AddResourceAsync("Meeting Room B");
        await _database.AddBlockAsync(roomA.Id, At(8), At(10), "Cleaning");
        await _database.AddBlockAsync(roomB.Id, At(12), At(13), "Event");
        await _database.AddBlockAsync(roomA.Id, At(18), At(20), "Out of range");

        await using var context = _database.CreateContext();
        var handler = new GetResourceBlocksQueryHandler(context, TestAccess.Admin);

        (await handler.Handle(new GetResourceBlocksQuery(At(9), At(18)), CancellationToken.None))
            .Select(b => b.Reason).Should().Equal("Cleaning", "Event");
        (await handler.Handle(new GetResourceBlocksQuery(At(9), At(18), roomB.Id), CancellationToken.None))
            .Select(b => b.Reason).Should().Equal("Event");
    }

    private static IReadOnlyList<string> ValidationErrors(DateTime start, DateTime end, string reason = "Maintenance") =>
        new CreateResourceBlockCommandValidator(new FakeTimeProvider(Now))
            .Validate(new CreateResourceBlockCommand(Guid.NewGuid(), start, end, reason))
            .Errors.Select(e => e.PropertyName).ToList();

    [Fact]
    public void Validator_StartInThePast_Fails() =>
        ValidationErrors(Now.UtcDateTime.AddHours(-2), Now.UtcDateTime.AddHours(1))
            .Should().Equal(nameof(CreateResourceBlockCommand.StartDateTime));

    [Fact]
    public void Validator_EndNotAfterStart_Fails() =>
        ValidationErrors(Now.UtcDateTime.AddHours(1), Now.UtcDateTime.AddHours(1))
            .Should().Equal(nameof(CreateResourceBlockCommand.EndDateTime));

    [Fact]
    public void Validator_LongerThan366Days_Fails() =>
        ValidationErrors(Now.UtcDateTime.AddHours(1), Now.UtcDateTime.AddDays(368))
            .Should().Equal(nameof(CreateResourceBlockCommand.EndDateTime));

    [Fact]
    public void Validator_RequiresAReason() =>
        ValidationErrors(Now.UtcDateTime.AddHours(1), Now.UtcDateTime.AddHours(3), "  ")
            .Should().Equal(nameof(CreateResourceBlockCommand.Reason));

    [Theory]
    [InlineData(42 * 24 + 1, true)] // six local weeks across the autumn clock change
    [InlineData(42 * 24 + 2, false)]
    public void QueryValidator_AllowsTheCalendarsSixWeeks_PlusAnHour(int hours, bool valid) =>
        new GetResourceBlocksQueryValidator().Validate(new GetResourceBlocksQuery(Day, Day.AddHours(hours)))
            .IsValid.Should().Be(valid);
}
