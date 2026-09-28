using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartDesk.API.Hubs;
using SmartDesk.API.Services;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Realtime;

public sealed class SignalRReservationNotifierTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IClientProxy> _everyone = new();
    private readonly Mock<IClientProxy> _groups = new();
    private readonly Mock<IClientProxy> _ownerClients = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly SignalRReservationNotifier _notifier;
    private IReadOnlyList<string>? _groupNames;

    public SignalRReservationNotifierTests()
    {
        _hubClients.Setup(c => c.All).Returns(_everyone.Object);
        _hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(_ownerClients.Object);
        _hubClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(names => _groupNames = names)
            .Returns(_groups.Object);
        var hub = new Mock<IHubContext<ReservationHub>>();
        hub.Setup(h => h.Clients).Returns(_hubClients.Object);
        _notifier = new SignalRReservationNotifier(hub.Object, _database.CreateContext(), NullLogger<SignalRReservationNotifier>.Instance);
    }

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(ReservationHub.Created)]
    [InlineData(ReservationHub.Updated)]
    [InlineData(ReservationHub.Cancelled)]
    public async Task A_shared_resources_event_goes_to_everyone_under_the_contract_name(string eventName)
    {
        var resource = await _database.AddResourceAsync();
        var reservation = Sample(resource.Id);

        await Send(eventName, reservation);

        _everyone.Verify(
            c => c.SendCoreAsync(
                eventName,
                It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], reservation)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _groups.VerifyNoOtherCalls();
        _ownerClients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_team_resources_event_goes_to_its_teams_the_admins_and_the_owner_only()
    {
        var zeynep = Guid.NewGuid();
        var okan = Guid.NewGuid();
        var resource = await _database.AddResourceAsync("Meeting Room B");
        await using (var context = _database.CreateContext())
        {
            context.ResourceTeams.AddRange(
                new ResourceTeam { ResourceId = resource.Id, TeamId = zeynep },
                new ResourceTeam { ResourceId = resource.Id, TeamId = okan });
            await context.SaveChangesAsync();
        }

        var reservation = Sample(resource.Id);

        await _notifier.ReservationCreatedAsync(reservation, CancellationToken.None);

        _groupNames.Should().BeEquivalentTo(ReservationHub.AdminsGroup, ReservationHub.TeamGroup(zeynep), ReservationHub.TeamGroup(okan));
        _groups.Verify(c => c.SendCoreAsync(ReservationHub.Created, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        // The owner keeps seeing their reservation after leaving the team, so they hear about it too.
        _hubClients.Verify(c => c.User(reservation.UserId.ToString()), Times.Once);
        _ownerClients.Verify(c => c.SendCoreAsync(ReservationHub.Created, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _everyone.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_reminder_goes_to_the_owner_only()
    {
        var resource = await _database.AddResourceAsync();
        var reservation = Sample(resource.Id);

        await _notifier.ReservationStartingSoonAsync(reservation, CancellationToken.None);

        _hubClients.Verify(c => c.User(reservation.UserId.ToString()), Times.Once);
        _ownerClients.Verify(
            c => c.SendCoreAsync(
                ReservationHub.StartingSoon,
                It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], reservation)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _everyone.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failed_broadcast_does_not_escape()
    {
        var resource = await _database.AddResourceAsync();
        _everyone
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("socket closed"));

        var act = () => _notifier.ReservationCreatedAsync(Sample(resource.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private Task Send(string eventName, ReservationDto reservation) => eventName switch
    {
        ReservationHub.Created => _notifier.ReservationCreatedAsync(reservation, CancellationToken.None),
        ReservationHub.Updated => _notifier.ReservationUpdatedAsync(reservation, CancellationToken.None),
        ReservationHub.Cancelled => _notifier.ReservationCancelledAsync(reservation, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(eventName)),
    };

    private static ReservationDto Sample(Guid resourceId) => new(
        Guid.CreateVersion7(),
        resourceId,
        "Meeting Room A",
        Guid.CreateVersion7(),
        "Meeting Room",
        "2nd Floor",
        Guid.CreateVersion7(),
        new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc),
        ReservationStatus.Confirmed,
        null,
        15);
}
