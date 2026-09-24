using FluentAssertions;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

public class ResourceValidatorTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static CreateResourceCommand ValidCreate() =>
        new("Meeting Room A", ResourceTypeSeedData.MeetingRoomId, 8, "2nd Floor", null, ["TV"]);

    private async Task<IReadOnlyList<string>> CreateErrorsAsync(CreateResourceCommand command)
    {
        await using var context = _database.CreateContext();
        var result = await new CreateResourceCommandValidator(context).ValidateAsync(command);
        return result.Errors.Select(e => e.PropertyName).ToList();
    }

    [Fact]
    public async Task Create_ValidCommand_Passes()
    {
        (await CreateErrorsAsync(ValidCreate())).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_UnknownResourceType_Fails()
    {
        (await CreateErrorsAsync(ValidCreate() with { ResourceTypeId = Guid.NewGuid() }))
            .Should().Equal(nameof(CreateResourceCommand.ResourceTypeId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task Create_CapacityOutOfRange_Fails(int capacity)
    {
        (await CreateErrorsAsync(ValidCreate() with { Capacity = capacity }))
            .Should().Equal(nameof(CreateResourceCommand.Capacity));
    }

    [Fact]
    public async Task Create_DuplicateFeaturesIgnoringCaseAndSpaces_Fails()
    {
        (await CreateErrorsAsync(ValidCreate() with { Features = ["TV", " tv"] }))
            .Should().Equal(nameof(CreateResourceCommand.Features));
    }

    [Fact]
    public async Task Create_BlankFeature_Fails()
    {
        (await CreateErrorsAsync(ValidCreate() with { Features = ["TV", "  "] }))
            .Should().ContainSingle(p => p.StartsWith(nameof(CreateResourceCommand.Features)));
    }

    [Fact]
    public async Task Update_UndefinedStatusValue_Fails()
    {
        var command = new UpdateResourceCommand(
            "Meeting Room A", ResourceTypeSeedData.MeetingRoomId, 8, "2nd Floor", null, null, (ResourceStatus)42);

        await using var context = _database.CreateContext();
        var result = await new UpdateResourceCommandValidator(context).ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateResourceCommand.Status));
    }

    [Theory]
    [InlineData(0)]   // end == start
    [InlineData(-60)] // end before start
    public void Available_EndNotAfterStart_Fails(int endOffsetMinutes)
    {
        var result = new GetAvailableResourcesQueryValidator()
            .Validate(new GetAvailableResourcesQuery(Start, Start.AddMinutes(endOffsetMinutes)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetAvailableResourcesQuery.EndDateTime));
    }

    [Fact]
    public void Available_WindowLongerThan31Days_Fails()
    {
        var result = new GetAvailableResourcesQueryValidator()
            .Validate(new GetAvailableResourcesQuery(Start, Start.AddDays(32)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetAvailableResourcesQuery.EndDateTime));
    }

    [Fact]
    public void Available_NonPositiveMinCapacity_Fails()
    {
        var result = new GetAvailableResourcesQueryValidator()
            .Validate(new GetAvailableResourcesQuery(Start, Start.AddHours(1), MinCapacity: 0));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(GetAvailableResourcesQuery.MinCapacity));
    }
}
