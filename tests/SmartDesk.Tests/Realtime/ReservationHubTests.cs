using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Moq;
using SmartDesk.API.Hubs;
using SmartDesk.Application.Common.Security;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Tests.Realtime;

// A connection joins the groups a team resource's events go to (ADR-0022): Admins, and the
// team the account is in now.
public class ReservationHubTests
{
    private static async Task<(List<string> Groups, HubConnectionRegistry Registry)> ConnectAsync(params Claim[] claims)
    {
        var joined = new List<string>();
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, group, _) => joined.Add(group))
            .Returns(Task.CompletedTask);
        var registry = new HubConnectionRegistry();
        var hub = new ReservationHub(registry)
        {
            Context = new FakeConnection(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))),
            Groups = groups.Object,
        };

        await hub.OnConnectedAsync();
        return (joined, registry);
    }

    [Fact]
    public async Task AManager_JoinsTheirTeamsGroup()
    {
        var managerId = Guid.NewGuid();

        var (groups, registry) = await ConnectAsync(
            new Claim(ClaimTypes.NameIdentifier, managerId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Manager),
            new Claim(SmartDeskClaims.Team, managerId.ToString()));

        groups.Should().Equal(ReservationHub.TeamGroup(managerId));
        registry.CountFor(managerId).Should().Be(1);
    }

    [Fact]
    public async Task AnAdmin_JoinsTheAdminsGroup()
    {
        var (groups, _) = await ConnectAsync(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, Roles.Admin));

        groups.Should().Equal(ReservationHub.AdminsGroup);
    }

    [Fact]
    public async Task AnEmployeeOutsideAnyTeam_JoinsNoGroup()
    {
        var (groups, _) = await ConnectAsync(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, Roles.Employee));

        groups.Should().BeEmpty();
    }

    private sealed class FakeConnection(ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId { get; } = Guid.NewGuid().ToString();
        public override string? UserIdentifier => User?.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal? User { get; } = user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
