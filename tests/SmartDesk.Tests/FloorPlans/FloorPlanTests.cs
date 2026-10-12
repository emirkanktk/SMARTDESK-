using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.FloorPlans.Commands.RemoveFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Commands.SetFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Common;
using SmartDesk.Application.Features.FloorPlans.Queries.GetFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Queries.GetFloors;
using SmartDesk.Application.Features.Resources.Commands.SetResourcePosition;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.FloorPlans;

// Floor plans (ADR-0041): the floors a person sees, the plans an Admin uploads, and where each
// resource is placed on its floor's plan.
public sealed class FloorPlanTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Zeynep = Guid.NewGuid();
    private static readonly Guid Okan = Guid.NewGuid();
    private static readonly FakeCurrentUser Admin = TestAccess.Admin;
    private static readonly FakeCurrentUser ZeynepManager = new(Zeynep, Roles.Manager) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee) { TeamId = Zeynep };
    private static readonly FakeCurrentUser Selin = new(Guid.NewGuid(), Roles.Employee) { TeamId = Okan };

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _time = new(Now);

    public void Dispose() => _database.Dispose();

    // A resource on a floor, shared or one team's, placed or not.
    private async Task<Resource> AddAsync(string name, string location, Guid? team = null, double? x = null, double? y = null)
    {
        var resource = await _database.AddResourceAsync(name);
        await using var context = _database.CreateContext();
        var tracked = await context.Resources.SingleAsync(r => r.Id == resource.Id);
        tracked.Location = location;
        tracked.MapX = x;
        tracked.MapY = y;
        if (team is { } teamId)
        {
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = resource.Id, TeamId = teamId });
        }

        await context.SaveChangesAsync();
        return tracked;
    }

    private async Task<IReadOnlyList<FloorDto>> FloorsAsync(FakeCurrentUser user)
    {
        await using var context = _database.CreateContext();
        return await new GetFloorsQueryHandler(context, user).Handle(new GetFloorsQuery(), CancellationToken.None);
    }

    private async Task<FloorDto> SetPlanAsync(FakeCurrentUser user, string location, byte[] data)
    {
        await using var context = _database.CreateContext();
        return await new SetFloorPlanImageCommandHandler(context, user, _time)
            .Handle(new SetFloorPlanImageCommand(location, data), CancellationToken.None);
    }

    private async Task<FloorPlanFile> PlanAsync(FakeCurrentUser user, string location)
    {
        await using var context = _database.CreateContext();
        return await new GetFloorPlanImageQueryHandler(context, user)
            .Handle(new GetFloorPlanImageQuery(location), CancellationToken.None);
    }

    private async Task RemovePlanAsync(FakeCurrentUser user, string location)
    {
        await using var context = _database.CreateContext();
        await new RemoveFloorPlanImageCommandHandler(context, user)
            .Handle(new RemoveFloorPlanImageCommand(location), CancellationToken.None);
    }

    private async Task<ResourceDto> PlaceAsync(FakeCurrentUser user, Resource resource, double? x, double? y)
    {
        await using var context = _database.CreateContext();
        return await new SetResourcePositionCommandHandler(context, user, TestAccess.Reader())
            .Handle(new SetResourcePositionCommand(x, y) { Id = resource.Id }, CancellationToken.None);
    }

    private static IReadOnlyList<string> PlanErrors(string location, byte[] data) =>
        new SetFloorPlanImageCommandValidator()
            .Validate(new SetFloorPlanImageCommand(location, data))
            .Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList();

    private static IReadOnlyList<string> PositionErrors(double? x, double? y) =>
        new SetResourcePositionCommandValidator()
            .Validate(new SetResourcePositionCommand(x, y))
            .Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList();

    // --- Floors ---

    [Fact]
    public async Task TheFloors_AreWhereTheResourcesSomeoneMayUseAre_WithTheirPlans()
    {
        await AddAsync("Meeting Room A", "2nd Floor");
        await AddAsync("Desk 1", "2nd Floor", x: 0.4, y: 0.6);
        await AddAsync("Meeting Room B", "5th Floor", team: Zeynep);
        await AddAsync("Company Car", "Garage", team: Okan);
        await SetPlanAsync(Admin, "5th Floor", TestImages.Png(1600, 900));
        await SetPlanAsync(Admin, "Roof", TestImages.Png(800, 600));

        var forAyse = await FloorsAsync(Ayse);
        var forAdmin = await FloorsAsync(Admin);

        forAyse.Should().Equal(
            new FloorDto("2nd Floor", 2, 1, null),
            new FloorDto("5th Floor", 1, 0, new FloorPlanImageDto(1600, 900, Now)));
        forAdmin.Select(f => (f.Location, f.Resources)).Should().Equal(
            ("2nd Floor", 2), ("5th Floor", 1), ("Garage", 1), ("Roof", 0));
    }

    // --- Plans ---

    [Fact]
    public async Task AnAdmin_SetsAPlan_WhoseSizeComesFromItsHeader_AndReplacesIt()
    {
        await AddAsync("Meeting Room A", "2nd Floor", x: 0.5, y: 0.5);

        var first = await SetPlanAsync(Admin, " 2nd Floor ", TestImages.Png(1600, 900));
        _time.Advance(TimeSpan.FromMinutes(5));
        var replaced = await SetPlanAsync(Admin, "2nd Floor", TestImages.Png(2000, 1000));

        first.Should().Be(new FloorDto("2nd Floor", 1, 1, new FloorPlanImageDto(1600, 900, Now)));
        replaced.Plan.Should().Be(new FloorPlanImageDto(2000, 1000, Now.AddMinutes(5)));
        var plan = await PlanAsync(Ayse, "2nd Floor");
        plan.ContentType.Should().Be("image/png");
        plan.Data.Should().Equal(TestImages.Png(2000, 1000));
        await using var context = _database.CreateContext();
        (await context.FloorPlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OnlyAnAdmin_ChangesAPlan()
    {
        await AddAsync("Meeting Room B", "5th Floor", team: Zeynep);

        var set = () => SetPlanAsync(ZeynepManager, "5th Floor", TestImages.Png(800, 600));
        var remove = () => RemovePlanAsync(ZeynepManager, "5th Floor");

        (await set.Should().ThrowAsync<ForbiddenAccessException>()).WithMessage("Only administrators can change a floor's plan.");
        await remove.Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task APlan_IsForThoseWhoMayUseAResourceOnItsFloor_AndForAdmins()
    {
        await AddAsync("Company Car", "Garage", team: Okan);
        await SetPlanAsync(Admin, "Garage", TestImages.Png(800, 600));
        await SetPlanAsync(Admin, "Roof", TestImages.Png(800, 600));

        (await PlanAsync(Selin, "Garage")).ContentType.Should().Be("image/png");
        (await PlanAsync(Admin, "Roof")).ContentType.Should().Be("image/png");
        var anotherTeams = () => PlanAsync(Ayse, "Garage");
        var nobodysYet = () => PlanAsync(Ayse, "Roof");
        (await anotherTeams.Should().ThrowAsync<NotFoundException>()).WithMessage("Floor plan 'Garage' was not found.");
        await nobodysYet.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RemovingAPlan_KeepsThePlaces_AndRemovingNoneIsFine()
    {
        await AddAsync("Desk 1", "2nd Floor", x: 0.4, y: 0.6);
        await SetPlanAsync(Admin, "2nd Floor", TestImages.Png(1600, 900));

        await RemovePlanAsync(Admin, "2nd Floor");
        await RemovePlanAsync(Admin, "2nd Floor");

        (await FloorsAsync(Admin)).Should().Equal(new FloorDto("2nd Floor", 1, 1, null));
    }

    [Fact]
    public void TheValidator_ExplainsAPlan_OnThePlanField()
    {
        PlanErrors("2nd Floor", []).Should().Equal("Plan: Choose an image of the floor plan.");
        PlanErrors("2nd Floor", "<svg/>"u8.ToArray()).Should().Equal("Plan: The plan must be a JPEG, PNG or WebP image.");
        PlanErrors("2nd Floor", TestImages.Png(5000, 1000)).Should().Equal("Plan: The plan can be at most 4096 pixels wide and 4096 pixels tall.");
        PlanErrors(" ", TestImages.Png(800, 600)).Should().Equal("Location: Say which floor the plan is for.");
        PlanErrors("2nd Floor", TestImages.Png(4096, 4096)).Should().BeEmpty();
    }

    [Fact]
    public void TheValidator_SpeaksTurkishToo()
    {
        using var _ = UiCulture.Use("tr-TR");

        PlanErrors("2nd Floor", []).Should().Equal("Plan: Kat planının resmini seçin.");
        PositionErrors(2, 0.5).Should().Equal(
            "Position: Planın içine yerleştirin: x ve y 0 ile 1 arasında olmalı; plandan kaldırmak için ikisini de boş bırakın.");
    }

    // --- Places ---

    [Fact]
    public async Task WhoeverMayEditTheResource_PlacesIt_AndNeitherXNorYTakesItOff()
    {
        var shared = await AddAsync("Meeting Room A", "2nd Floor");
        var zeyneps = await AddAsync("Meeting Room B", "5th Floor", team: Zeynep);

        (await PlaceAsync(Admin, shared, 0.25, 0.5)).Position.Should().Be(new ResourcePositionDto(0.25, 0.5));
        (await PlaceAsync(ZeynepManager, zeyneps, 1, 0)).Position.Should().Be(new ResourcePositionDto(1, 0));
        (await PlaceAsync(Admin, shared, null, null)).Position.Should().BeNull();

        var managerOnShared = () => PlaceAsync(ZeynepManager, shared, 0.5, 0.5);
        var employee = () => PlaceAsync(Ayse, zeyneps, 0.5, 0.5);
        var anotherTeam = () => PlaceAsync(Selin, zeyneps, 0.5, 0.5);
        await managerOnShared.Should().ThrowAsync<ForbiddenAccessException>("a shared resource is the Admins' to manage");
        await employee.Should().ThrowAsync<ForbiddenAccessException>();
        await anotherTeam.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(0.0, 0.0, true)]
    [InlineData(1.0, 1.0, true)]
    [InlineData(-0.01, 0.5, false)]
    [InlineData(0.5, 1.01, false)]
    [InlineData(0.5, null, false)]
    [InlineData(null, 0.5, false)]
    [InlineData(null, null, true)]
    public void APlace_IsInsideThePlan_OrNone(double? x, double? y, bool valid)
    {
        if (valid)
        {
            PositionErrors(x, y).Should().BeEmpty();
        }
        else
        {
            PositionErrors(x, y).Should().Equal(
                "Position: Place it inside the plan, with x and y from 0 to 1, or leave out both to take it off.");
        }
    }

    [Fact]
    public async Task MovingToAnotherLocation_TakesItOffThePlan_AndStayingKeepsItsPlace()
    {
        var room = await AddAsync("Board Room", "2nd Floor", x: 0.3, y: 0.7);

        async Task<ResourceDto> UpdateAsync(string location)
        {
            await using var context = _database.CreateContext();
            return await new UpdateResourceCommandHandler(context, Admin, TestAccess.Reader(), _time).Handle(
                new UpdateResourceCommand("Board Room", ResourceTypeSeedData.MeetingRoomId, 8, location, null, null, ResourceStatus.Active)
                {
                    Id = room.Id,
                },
                CancellationToken.None);
        }

        (await UpdateAsync("2nd Floor")).Position.Should().Be(new ResourcePositionDto(0.3, 0.7));
        (await UpdateAsync("5th Floor")).Position.Should().BeNull("its place was on the 2nd floor's plan");
    }
}
