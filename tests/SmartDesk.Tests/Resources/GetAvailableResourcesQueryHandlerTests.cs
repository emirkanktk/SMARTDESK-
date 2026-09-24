using FluentAssertions;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

public class GetAvailableResourcesQueryHandlerTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private async Task<IReadOnlyList<string>> AvailableNamesAsync(GetAvailableResourcesQuery query)
    {
        await using var context = _database.CreateContext();
        var result = await new GetAvailableResourcesQueryHandler(context).Handle(query, CancellationToken.None);
        return result.Select(r => r.Name).ToList();
    }

    [Theory]
    [InlineData(11, 0, 12, 0)]  // the brief's example: existing 10:00-11:30, request 11:00-12:00
    [InlineData(9, 0, 10, 30)]  // overlaps the start
    [InlineData(10, 15, 10, 45)] // entirely inside the existing booking
    [InlineData(9, 0, 13, 0)]   // entirely covers the existing booking
    [InlineData(10, 0, 11, 30)] // exactly the same range
    public async Task Handle_RangeOverlappingConfirmedReservation_ExcludesResource(
        int startHour, int startMinute, int endHour, int endMinute)
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30));

        var names = await AvailableNamesAsync(new(At(startHour, startMinute), At(endHour, endMinute)));

        names.Should().NotContain("Meeting Room A");
    }

    [Theory]
    [InlineData(11, 30, 12, 30)] // starts exactly when the existing booking ends
    [InlineData(8, 0, 10, 0)]    // ends exactly when the existing booking starts
    [InlineData(14, 0, 15, 0)]   // unrelated time
    public async Task Handle_BackToBackOrSeparateRange_KeepsResourceAvailable(
        int startHour, int startMinute, int endHour, int endMinute)
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30));

        var names = await AvailableNamesAsync(new(At(startHour, startMinute), At(endHour, endMinute)));

        names.Should().Contain("Meeting Room A");
    }

    [Fact]
    public async Task Handle_OverlappingReservationWasCancelled_ResourceIsAvailable()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30), ReservationStatus.Cancelled);

        var names = await AvailableNamesAsync(new(At(11), At(12)));

        names.Should().Contain("Meeting Room A");
    }

    [Fact]
    public async Task Handle_ReservationOnAnotherResource_DoesNotAffectThisOne()
    {
        var roomA = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddResourceAsync("Meeting Room B");
        await _database.AddReservationAsync(roomA.Id, At(10), At(11));

        var names = await AvailableNamesAsync(new(At(10), At(11)));

        names.Should().Equal("Meeting Room B");
    }

    [Theory]
    [InlineData(ResourceStatus.Inactive)]
    [InlineData(ResourceStatus.Maintenance)]
    public async Task Handle_ResourceNotActive_IsNeverAvailable(ResourceStatus status)
    {
        await _database.AddResourceAsync("Camera C-1", ResourceTypeSeedData.CameraId, 1, status);

        var names = await AvailableNamesAsync(new(At(10), At(11)));

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TypeAndCapacityFilters_AreApplied()
    {
        await _database.AddResourceAsync("Meeting Room A", ResourceTypeSeedData.MeetingRoomId, 8);
        await _database.AddResourceAsync("Meeting Room B", ResourceTypeSeedData.MeetingRoomId, 4);
        await _database.AddResourceAsync("Desk 3-01", ResourceTypeSeedData.DeskId, 1);

        var names = await AvailableNamesAsync(new(At(10), At(11), ResourceTypeSeedData.MeetingRoomId, MinCapacity: 5));

        names.Should().Equal("Meeting Room A");
    }

    [Fact]
    public async Task Handle_LocalTimeInput_IsComparedAsUtc()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11, 30));

        // Query-string binding hands the handler local times; the same instants must still clash.
        var names = await AvailableNamesAsync(new(At(11).ToLocalTime(), At(12).ToLocalTime()));

        names.Should().NotContain("Meeting Room A");
    }
}
