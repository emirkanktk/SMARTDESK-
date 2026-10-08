using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartDesk.API.Hubs;
using SmartDesk.API.Services;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Realtime;

// Reports go to those who may manage their resource, and nobody else (ADR-0037).
public sealed class SignalRIssueNotifierTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IClientProxy> _admins = new();
    private readonly Dictionary<string, Mock<IClientProxy>> _users = [];
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly SignalRIssueNotifier _notifier;

    public SignalRIssueNotifierTests()
    {
        _hubClients.Setup(c => c.Group(ReservationHub.AdminsGroup)).Returns(_admins.Object);
        _hubClients.Setup(c => c.User(It.IsAny<string>())).Returns((string id) =>
        {
            if (!_users.TryGetValue(id, out var proxy))
            {
                _users[id] = proxy = new Mock<IClientProxy>();
            }

            return proxy.Object;
        });
        var hub = new Mock<IHubContext<ReservationHub>>();
        hub.Setup(h => h.Clients).Returns(_hubClients.Object);
        _notifier = new SignalRIssueNotifier(hub.Object, _database.CreateContext(), NullLogger<SignalRIssueNotifier>.Instance);
    }

    public void Dispose() => _database.Dispose();

    private static IssueReportDto Sample(Guid resourceId) => new(
        Guid.CreateVersion7(), resourceId, "Meeting Room B", IssueCategory.Display, "The TV doesn't turn on", true,
        IssueStatus.Open, false, Guid.NewGuid(), new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc), null);

    private async Task<Resource> AddRoomAsync(params Guid[] teams)
    {
        var resource = await _database.AddResourceAsync("Meeting Room B");
        await using var context = _database.CreateContext();
        context.ResourceTeams.AddRange(teams.Select(team => new ResourceTeam { ResourceId = resource.Id, TeamId = team }));
        await context.SaveChangesAsync();
        return resource;
    }

    [Fact]
    public async Task A_report_on_a_teams_own_resource_goes_to_the_admins_and_its_manager()
    {
        var selin = Guid.NewGuid();
        var room = await AddRoomAsync(selin);
        var issue = Sample(room.Id);

        await _notifier.IssueReportedAsync(issue, CancellationToken.None);

        _admins.Verify(
            c => c.SendCoreAsync(
                ReservationHub.IssueReported,
                It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], issue)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _users.Keys.Should().Equal(selin.ToString());
        _users[selin.ToString()].Verify(
            c => c.SendCoreAsync(ReservationHub.IssueReported, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _hubClients.Verify(c => c.All, Times.Never);
    }

    [Fact]
    public async Task A_shared_resource_or_one_several_teams_share_is_the_admins_alone()
    {
        var shared = await _database.AddResourceAsync("Board Room");
        var sharedByTwo = await AddRoomAsync(Guid.NewGuid(), Guid.NewGuid());

        await _notifier.IssueUpdatedAsync(Sample(shared.Id), CancellationToken.None);
        await _notifier.IssueUpdatedAsync(Sample(sharedByTwo.Id), CancellationToken.None);

        _admins.Verify(c => c.SendCoreAsync(ReservationHub.IssueUpdated, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _users.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_push_does_not_escape_or_stop_the_manager_hearing()
    {
        var selin = Guid.NewGuid();
        var room = await AddRoomAsync(selin);
        _admins.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("socket closed"));

        var act = () => _notifier.IssueReportedAsync(Sample(room.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _users[selin.ToString()].Verify(
            c => c.SendCoreAsync(ReservationHub.IssueReported, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
