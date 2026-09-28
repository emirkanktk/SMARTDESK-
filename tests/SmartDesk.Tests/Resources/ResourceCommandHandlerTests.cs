using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

public class ResourceCommandHandlerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Create_StoresActiveResourceWithTrimmedDistinctFeatures()
    {
        var command = new CreateResourceCommand(
            "  Meeting Room A ", ResourceTypeSeedData.MeetingRoomId, 8, "2nd Floor", "  ",
            ["TV", "Video Conference", "tv ", ""]);

        await using var context = _database.CreateContext();
        var dto = await new CreateResourceCommandHandler(context, TestAccess.Admin, new Moq.Mock<SmartDesk.Application.Common.Interfaces.IUserAccounts>(Moq.MockBehavior.Strict).Object, TestAccess.Reader()).Handle(command, CancellationToken.None);

        dto.Name.Should().Be("Meeting Room A");
        dto.ResourceTypeName.Should().Be("Meeting Room");
        dto.Status.Should().Be(ResourceStatus.Active);
        dto.Description.Should().BeNull();
        dto.Features.Should().Equal("TV", "Video Conference");

        await using var verify = _database.CreateContext();
        (await verify.ResourceFeatures.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Update_UnknownId_ThrowsNotFound()
    {
        var command = new UpdateResourceCommand(
            "Room", ResourceTypeSeedData.MeetingRoomId, 4, "1st Floor", null, null, ResourceStatus.Active)
        { Id = Guid.NewGuid() };

        await using var context = _database.CreateContext();
        var act = () => new UpdateResourceCommandHandler(context, TestAccess.Admin, TestAccess.Reader()).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_ReplacesFieldsStatusAndFeatures_KeepingUnchangedFeatureRows()
    {
        var resource = await _database.AddResourceAsync("Meeting Room A", features: ["TV", "Whiteboard"]);
        Guid tvFeatureId;
        await using (var before = _database.CreateContext())
        {
            tvFeatureId = await before.ResourceFeatures.Where(f => f.Name == "TV").Select(f => f.Id).SingleAsync();
        }

        var command = new UpdateResourceCommand(
            "Meeting Room A (renovated)", ResourceTypeSeedData.MeetingRoomId, 10, "3rd Floor", "Bigger now",
            ["tv", "Video Conference"], ResourceStatus.Maintenance)
        { Id = resource.Id };

        await using var context = _database.CreateContext();
        var dto = await new UpdateResourceCommandHandler(context, TestAccess.Admin, TestAccess.Reader()).Handle(command, CancellationToken.None);

        dto.Name.Should().Be("Meeting Room A (renovated)");
        dto.Capacity.Should().Be(10);
        dto.Location.Should().Be("3rd Floor");
        dto.Status.Should().Be(ResourceStatus.Maintenance);
        // Order-agnostic: feature order follows the database collation (SQLite sorts "V" before "t").
        dto.Features.Should().BeEquivalentTo("tv", "Video Conference");

        await using var verify = _database.CreateContext();
        var features = await verify.ResourceFeatures.Where(f => f.ResourceId == resource.Id).ToListAsync();
        features.Select(f => f.Name).Should().BeEquivalentTo("tv", "Video Conference");
        features.Should().Contain(f => f.Id == tvFeatureId, "an unchanged feature (only its casing differs) keeps its row");
    }
}
