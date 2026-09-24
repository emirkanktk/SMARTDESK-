using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

public class ChangeResourceStatusCommandHandlerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<Application.Features.Resources.Common.ResourceDto> ChangeAsync(Guid id, ResourceStatus status)
    {
        await using var context = _database.CreateContext();
        return await new ChangeResourceStatusCommandHandler(context)
            .Handle(new ChangeResourceStatusCommand(status) { Id = id }, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ChangesOnlyTheStatus()
    {
        var room = await _database.AddResourceAsync("Meeting Room A", capacity: 8, features: ["TV"]);

        // Another admin renames the room after this admin loaded the list...
        await using (var other = _database.CreateContext())
        {
            var loaded = await other.Resources.SingleAsync(r => r.Id == room.Id);
            loaded.Name = "Meeting Room A (renovated)";
            await other.SaveChangesAsync();
        }

        // ...and this admin deactivates it: the rename must survive.
        var dto = await ChangeAsync(room.Id, ResourceStatus.Inactive);

        dto.Status.Should().Be(ResourceStatus.Inactive);
        dto.Name.Should().Be("Meeting Room A (renovated)");
        dto.Capacity.Should().Be(8);
        dto.Features.Should().Equal("TV");
    }

    [Fact]
    public async Task Handle_SameStatus_IsANoOp()
    {
        var room = await _database.AddResourceAsync(status: ResourceStatus.Maintenance);

        var dto = await ChangeAsync(room.Id, ResourceStatus.Maintenance);

        dto.Status.Should().Be(ResourceStatus.Maintenance);
    }

    [Fact]
    public async Task Handle_UnknownResource_ThrowsNotFound()
    {
        var act = () => ChangeAsync(Guid.NewGuid(), ResourceStatus.Active);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Validator_UndefinedStatus_Fails() =>
        new ChangeResourceStatusCommandValidator().Validate(new ChangeResourceStatusCommand((ResourceStatus)42))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(ChangeResourceStatusCommand.Status));
}
