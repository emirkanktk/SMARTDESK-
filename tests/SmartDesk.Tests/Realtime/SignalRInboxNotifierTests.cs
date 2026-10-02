using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartDesk.API.Hubs;
using SmartDesk.API.Services;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Tests.Realtime;

// Inbox messages go to their recipient's connections only (ADR-0032).
public sealed class SignalRInboxNotifierTests
{
    private readonly Mock<IHubClients> _clients = new();
    private readonly Dictionary<string, Mock<IClientProxy>> _users = [];
    private readonly SignalRInboxNotifier _notifier;

    public SignalRInboxNotifierTests()
    {
        _clients.Setup(c => c.User(It.IsAny<string>())).Returns((string id) =>
        {
            if (!_users.TryGetValue(id, out var proxy))
            {
                _users[id] = proxy = new Mock<IClientProxy>();
            }

            return proxy.Object;
        });
        var hub = new Mock<IHubContext<ReservationHub>>();
        hub.Setup(h => h.Clients).Returns(_clients.Object);
        _notifier = new SignalRInboxNotifier(hub.Object, NullLogger<SignalRInboxNotifier>.Instance);
    }

    private static InboxMessageDto Message(Guid recipientId) => new(
        Guid.CreateVersion7(), recipientId, InboxMessageKind.MeetingInvitation, Guid.NewGuid(), Guid.NewGuid(),
        "Board Room", new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 11, 0, 0, DateTimeKind.Utc),
        null, null, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), null);

    [Fact]
    public async Task Each_message_goes_to_its_recipient_only()
    {
        var ayse = Message(Guid.NewGuid());
        var mehmet = Message(Guid.NewGuid());

        await _notifier.MessagesSentAsync([ayse, mehmet], CancellationToken.None);

        _clients.Verify(c => c.User(It.IsAny<string>()), Times.Exactly(2));
        foreach (var message in new[] { ayse, mehmet })
        {
            _users[message.RecipientId.ToString()].Verify(
                p => p.SendCoreAsync(
                    ReservationHub.InboxMessageReceived,
                    It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], message)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        _clients.Verify(c => c.All, Times.Never);
    }

    [Fact]
    public async Task A_failed_push_does_not_escape_or_stop_the_others()
    {
        var first = Message(Guid.NewGuid());
        var second = Message(Guid.NewGuid());
        _clients.Object.User(first.RecipientId.ToString());
        _users[first.RecipientId.ToString()]
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("socket closed"));

        var act = () => _notifier.MessagesSentAsync([first, second], CancellationToken.None);

        await act.Should().NotThrowAsync();
        _users[second.RecipientId.ToString()].Verify(
            p => p.SendCoreAsync(ReservationHub.InboxMessageReceived, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
