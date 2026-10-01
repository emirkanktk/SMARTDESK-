using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Queries.GetReservationById;
using SmartDesk.Application.Features.Reservations.Queries.GetReservations;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;
using SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.SetResourceTeams;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Resources.Queries.GetResourceById;
using SmartDesk.Application.Features.Resources.Queries.GetResources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Resources;

// Who may see, book and manage which resource (ADR-0022): a team's resources are its own, a
// resource with no teams is everyone's, one with several teams is theirs, first come first
// served. Two teams, Zeynep's and Okan's, on the real handlers over SQLite.
public sealed class TeamResourceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day = Now.Date;

    private static readonly Guid Zeynep = Guid.NewGuid();
    private static readonly Guid Okan = Guid.NewGuid();

    private static readonly FakeCurrentUser Admin = TestAccess.Admin;
    private static readonly FakeCurrentUser ZeynepManager = new(Zeynep, Roles.Manager) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Selin = new(Guid.NewGuid(), Roles.Employee) { TeamId = Okan };
    private static readonly FakeCurrentUser Loner = new(Guid.NewGuid(), Roles.Employee);

    private static readonly FakeIdentityService Names = new(new Dictionary<Guid, string>
    {
        [Zeynep] = "Zeynep Kaya",
        [Okan] = "Okan Demir",
    });

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IReservationNotifier> _notifier = new();
    private Resource _shared = null!;
    private Resource _zeyneps = null!;
    private Resource _okans = null!;
    private Resource _both = null!;

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync()
    {
        _shared = await _database.AddResourceAsync("Board Room");
        _zeyneps = await _database.AddResourceAsync("Meeting Room B");
        _okans = await _database.AddResourceAsync("Studio", ResourceTypeSeedData.MeetingRoomId);
        _both = await _database.AddResourceAsync("Projector P-1", ResourceTypeSeedData.ProjectorId, capacity: 1);
        await using var context = _database.CreateContext();
        context.ResourceTeams.AddRange(
            new ResourceTeam { ResourceId = _zeyneps.Id, TeamId = Zeynep },
            new ResourceTeam { ResourceId = _okans.Id, TeamId = Okan },
            new ResourceTeam { ResourceId = _both.Id, TeamId = Zeynep },
            new ResourceTeam { ResourceId = _both.Id, TeamId = Okan });
        await context.SaveChangesAsync();
    }

    private static DateTime At(int hour) => Day.AddHours(hour);

    private async Task<IReadOnlyList<string>> VisibleTo(FakeCurrentUser user)
    {
        await using var context = _database.CreateContext();
        var resources = await new GetResourcesQueryHandler(context, user, TestAccess.Reader(Names))
            .Handle(new GetResourcesQuery(), CancellationToken.None);
        return resources.Select(r => r.Name).ToList();
    }

    private async Task<Guid> BookAsync(FakeCurrentUser user, Resource resource, int from, int to)
    {
        await using var context = _database.CreateContext();
        var dto = await new CreateReservationCommandHandler(context, user, Names, _notifier.Object, TestInvitations.For(context, user))
            .Handle(new CreateReservationCommand(resource.Id, At(from), At(to), null), CancellationToken.None);
        return dto.Id;
    }

    private static Mock<IUserAccounts> Managers(params Guid[] active)
    {
        var accounts = new Mock<IUserAccounts>();
        accounts.Setup(a => a.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => active.Contains(id)
                ? new UserAccount(id, "M", "Anager", "m@x", Roles.Manager, AccountStatus.Active, null, null, Now, 0)
                : new UserAccount(id, "E", "Mployee", "e@x", Roles.Employee, AccountStatus.Active, null, null, Now, 0));
        return accounts;
    }

    [Fact]
    public async Task EachUser_SeesTheSharedResources_AndTheirOwnTeams_Only()
    {
        await SeedAsync();

        (await VisibleTo(Ayse)).Should().BeEquivalentTo("Board Room", "Meeting Room B", "Projector P-1");
        (await VisibleTo(Selin)).Should().BeEquivalentTo("Board Room", "Studio", "Projector P-1");
        (await VisibleTo(ZeynepManager)).Should().BeEquivalentTo("Board Room", "Meeting Room B", "Projector P-1");
        (await VisibleTo(Loner)).Should().Equal("Board Room");
        (await VisibleTo(Admin)).Should().HaveCount(4);
    }

    [Fact]
    public async Task Resources_NameTheirTeams_AndSharedOnesHaveNone()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var resources = await new GetResourcesQueryHandler(context, Admin, TestAccess.Reader(Names))
            .Handle(new GetResourcesQuery(), CancellationToken.None);

        resources.Single(r => r.Name == "Board Room").Teams.Should().BeEmpty();
        resources.Single(r => r.Name == "Projector P-1").Teams.Select(t => t.ManagerName)
            .Should().Equal("Okan Demir", "Zeynep Kaya");
    }

    [Fact]
    public async Task AnotherTeamsResource_IsNotFound_ByIdOrInAvailability()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();

        var byId = () => new GetResourceByIdQueryHandler(context, Selin, TestAccess.Reader())
            .Handle(new GetResourceByIdQuery(_zeyneps.Id), CancellationToken.None);
        await byId.Should().ThrowAsync<NotFoundException>();

        var available = await new GetAvailableResourcesQueryHandler(context, Selin, TestAccess.Reader())
            .Handle(new GetAvailableResourcesQuery(At(10), At(11), null, null), CancellationToken.None);
        available.Select(r => r.Name).Should().BeEquivalentTo("Board Room", "Studio", "Projector P-1");
    }

    [Fact]
    public async Task AnotherTeamsResource_CantBeBooked()
    {
        await SeedAsync();

        var act = () => BookAsync(Selin, _zeyneps, 10, 11);

        await act.Should().ThrowAsync<NotFoundException>();
        await using var context = _database.CreateContext();
        (await context.Reservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AResourceTwoTeamsShare_IsBookedByAvailability_FirstComeFirstServed()
    {
        await SeedAsync();

        await BookAsync(Ayse, _both, 10, 11);
        var clash = () => BookAsync(Selin, _both, 10, 11);
        await clash.Should().ThrowAsync<ConflictException>().WithMessage("*already reserved*");

        await BookAsync(Selin, _both, 11, 12); // back to back is fine
    }

    [Fact]
    public async Task TheCalendar_ShowsOnlyBookingsOnResourcesTheCallerMayUse()
    {
        await SeedAsync();
        await _database.AddReservationAsync(_shared.Id, At(9), At(10));
        await _database.AddReservationAsync(_zeyneps.Id, At(10), At(11));
        await _database.AddReservationAsync(_okans.Id, At(11), At(12));
        await using var context = _database.CreateContext();

        var forSelin = await new GetReservationsQueryHandler(context, Selin, Names)
            .Handle(new GetReservationsQuery(Day, Day.AddDays(1)), CancellationToken.None);

        forSelin.Select(r => r.ResourceName).Should().BeEquivalentTo("Board Room", "Studio");
    }

    [Fact]
    public async Task AReservation_IsVisibleToItsOwner_EvenOnAResourceTheyCanNoLongerUse()
    {
        await SeedAsync();
        var selinsBooking = await _database.AddReservationAsync(_zeyneps.Id, At(10), At(11), userId: Selin.UserId);
        await using var context = _database.CreateContext();

        var own = await new GetReservationByIdQueryHandler(context, Selin, Names)
            .Handle(new GetReservationByIdQuery(selinsBooking.Id), CancellationToken.None);
        own.ResourceName.Should().Be("Meeting Room B");

        var otherTeam = () => new GetReservationByIdQueryHandler(context, new FakeCurrentUser(Guid.NewGuid(), Roles.Employee) { TeamId = Okan }, Names)
            .Handle(new GetReservationByIdQuery(selinsBooking.Id), CancellationToken.None);
        await otherTeam.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task TheDashboard_CountsAndListsOnlyUsableResources()
    {
        await SeedAsync();
        await _database.AddReservationAsync(_okans.Id, Now.AddHours(1), Now.AddHours(2));
        await using var context = _database.CreateContext();

        var dashboard = await new GetDashboardQueryHandler(context, Ayse, Names, new FakeTimeProvider(new DateTimeOffset(Now)))
            .Handle(new GetDashboardQuery(null), CancellationToken.None);

        dashboard.Stats.ActiveResources.Should().Be(3);
        dashboard.Today.Should().BeEmpty("Okan's studio isn't Ayşe's to see");
    }

    [Fact]
    public async Task AManager_BlocksOnlyWhatBelongsToTheirTeamAlone()
    {
        await SeedAsync();

        async Task Block(Resource resource)
        {
            await using var context = _database.CreateContext();
            await new CreateResourceBlockCommandHandler(context, ZeynepManager)
                .Handle(new CreateResourceBlockCommand(resource.Id, At(13), At(14), "Cleaning"), CancellationToken.None);
        }

        await Block(_zeyneps);
        await FluentActions.Awaiting(() => Block(_both)).Should().ThrowAsync<ForbiddenAccessException>();
        await FluentActions.Awaiting(() => Block(_shared)).Should().ThrowAsync<ForbiddenAccessException>();
        await FluentActions.Awaiting(() => Block(_okans)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Blocks_OnAnotherTeamsResource_AreHidden_AndCantBeRemovedByTheirManager()
    {
        await SeedAsync();
        var okansBlock = await _database.AddBlockAsync(_okans.Id, At(13), At(14));
        var sharedBlock = await _database.AddBlockAsync(_shared.Id, At(15), At(16));
        await using var context = _database.CreateContext();

        var visible = await new GetResourceBlocksQueryHandler(context, Ayse)
            .Handle(new GetResourceBlocksQuery(Day, Day.AddDays(1)), CancellationToken.None);
        visible.Select(b => b.Id).Should().Equal(sharedBlock.Id);

        var removeOkans = () => new DeleteResourceBlockCommandHandler(context, ZeynepManager)
            .Handle(new DeleteResourceBlockCommand(okansBlock.Id), CancellationToken.None);
        var removeShared = () => new DeleteResourceBlockCommandHandler(context, ZeynepManager)
            .Handle(new DeleteResourceBlockCommand(sharedBlock.Id), CancellationToken.None);
        await removeOkans.Should().ThrowAsync<NotFoundException>();
        await removeShared.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task AManagersNewResource_BelongsToTheirTeam_AndNobodyElses()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();
        var handler = new CreateResourceCommandHandler(context, ZeynepManager, Managers(Zeynep, Okan).Object, TestAccess.Reader(Names));

        var created = await handler.Handle(
            new CreateResourceCommand("Team Desk", ResourceTypeSeedData.DeskId, 1, "3rd Floor", null, null), CancellationToken.None);
        created.Teams.Should().ContainSingle().Which.Should().Be(new ResourceTeamDto(Zeynep, "Zeynep Kaya"));

        var forOkan = () => handler.Handle(
            new CreateResourceCommand("Sneaky", ResourceTypeSeedData.DeskId, 1, "3rd Floor", null, null, [Okan]), CancellationToken.None);
        await forOkan.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task AnAdmin_ChoosesTheTeams_WhichMustBeActiveManagers()
    {
        await SeedAsync();
        await using var context = _database.CreateContext();
        var handler = new CreateResourceCommandHandler(context, Admin, Managers(Zeynep, Okan).Object, TestAccess.Reader(Names));

        var both = await handler.Handle(
            new CreateResourceCommand("Van", ResourceTypeSeedData.CompanyCarId, 3, "Parking", null, null, [Zeynep, Okan, Zeynep]),
            CancellationToken.None);
        both.Teams.Select(t => t.Id).Should().BeEquivalentTo([Zeynep, Okan]);

        var shared = await handler.Handle(
            new CreateResourceCommand("Kitchen", ResourceTypeSeedData.MeetingRoomId, 10, "1st Floor", null, null), CancellationToken.None);
        shared.Teams.Should().BeEmpty();

        var notAManager = () => handler.Handle(
            new CreateResourceCommand("X", ResourceTypeSeedData.DeskId, 1, "3rd Floor", null, null, [Ayse.UserId!.Value]), CancellationToken.None);
        (await notAManager.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "TeamIds");
    }

    [Fact]
    public async Task AManager_EditsAndSwitchesOffOnlyWhatBelongsToTheirTeamAlone()
    {
        await SeedAsync();

        async Task Edit(Resource resource)
        {
            await using var context = _database.CreateContext();
            await new UpdateResourceCommandHandler(context, ZeynepManager, TestAccess.Reader(), TimeProvider.System)
                .Handle(new UpdateResourceCommand("Renamed", resource.ResourceTypeId, 4, "2nd Floor", null, null, ResourceStatus.Active) { Id = resource.Id }, CancellationToken.None);
        }

        async Task SwitchOff(Resource resource)
        {
            await using var context = _database.CreateContext();
            await new ChangeResourceStatusCommandHandler(context, ZeynepManager, TestAccess.Reader(), TimeProvider.System)
                .Handle(new ChangeResourceStatusCommand(ResourceStatus.Inactive) { Id = resource.Id }, CancellationToken.None);
        }

        await Edit(_zeyneps);
        await SwitchOff(_zeyneps);
        foreach (var notHers in new[] { _both, _shared })
        {
            await FluentActions.Awaiting(() => Edit(notHers)).Should().ThrowAsync<ForbiddenAccessException>();
            await FluentActions.Awaiting(() => SwitchOff(notHers)).Should().ThrowAsync<ForbiddenAccessException>();
        }

        await FluentActions.Awaiting(() => Edit(_okans)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => SwitchOff(_okans)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AnAdmin_ReassignsTeams_OrMakesAResourceShared()
    {
        await SeedAsync();

        async Task<ResourceDto> SetTeams(Resource resource, params Guid[] teams)
        {
            await using var context = _database.CreateContext();
            return await new SetResourceTeamsCommandHandler(context, Managers(Zeynep, Okan).Object, TestAccess.Reader(Names))
                .Handle(new SetResourceTeamsCommand(teams) { Id = resource.Id }, CancellationToken.None);
        }

        (await SetTeams(_zeyneps, Okan)).Teams.Select(t => t.Id).Should().Equal(Okan);
        (await VisibleTo(Ayse)).Should().NotContain("Meeting Room B");
        (await VisibleTo(Selin)).Should().Contain("Meeting Room B");

        (await SetTeams(_zeyneps)).Teams.Should().BeEmpty();
        (await VisibleTo(Loner)).Should().Contain("Meeting Room B");

        var invalid = () => SetTeams(_zeyneps, Ayse.UserId!.Value);
        await invalid.Should().ThrowAsync<ValidationException>();
    }
}
