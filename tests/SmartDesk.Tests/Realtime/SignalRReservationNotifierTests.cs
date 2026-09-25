using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartDesk.API.Hubs;
using SmartDesk.API.Services;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Tests.Realtime;

public class SignalRReservationNotifierTests
{
    private readonly Mock<IClientProxy> _clients = new();
    private readonly SignalRReservationNotifier _notifier;

    public SignalRReservationNotifierTests()
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(_clients.Object);
        var hub = new Mock<IHubContext<ReservationHub>>();
        hub.Setup(h => h.Clients).Returns(hubClients.Object);
        _notifier = new SignalRReservationNotifier(hub.Object, NullLogger<SignalRReservationNotifier>.Instance);
    }

    [Theory]
    [InlineData(ReservationHub.Created)]
    [InlineData(ReservationHub.Updated)]
    [InlineData(ReservationHub.Cancelled)]
    public async Task Broadcasts_the_reservation_under_the_contract_event_name(string eventName)
    {
        var reservation = Sample();

        await Send(eventName, reservation);

        _clients.Verify(
            c => c.SendCoreAsync(
                eventName,
                It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], reservation)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_failed_broadcast_does_not_escape()
    {
        _clients
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("socket closed"));

        var act = () => _notifier.ReservationCreatedAsync(Sample(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private Task Send(string eventName, ReservationDto reservation) => eventName switch
    {
        ReservationHub.Created => _notifier.ReservationCreatedAsync(reservation, CancellationToken.None),
        ReservationHub.Updated => _notifier.ReservationUpdatedAsync(reservation, CancellationToken.None),
        ReservationHub.Cancelled => _notifier.ReservationCancelledAsync(reservation, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(eventName)),
    };

    private static ReservationDto Sample() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Meeting Room A",
        Guid.CreateVersion7(),
        "Meeting Room",
        "2nd Floor",
        Guid.CreateVersion7(),
        new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc),
        ReservationStatus.Confirmed,
        null);
}
