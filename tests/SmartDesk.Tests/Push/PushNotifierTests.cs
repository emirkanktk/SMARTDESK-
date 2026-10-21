using FluentAssertions;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Push;

// What goes to people's devices (ADR-0056): what reaches one person about their booking, and every
// new inbox message; never the events every open screen gets.
public sealed class PushNotifierTests
{
    private static readonly DateTime Start = new(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc);

    private readonly CapturingPushSender _push = new();

    private static ReservationDto Booking() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Board Room", Guid.NewGuid(), "Meeting Room", "3rd Floor", Guid.NewGuid(),
        Start, Start.AddHours(1), ReservationStatus.Confirmed, null, 15, true, null, null);

    [Fact]
    public async Task What_reaches_the_owner_goes_to_their_devices_and_events_for_every_screen_do_not()
    {
        var notifier = new PushReservationNotifier(_push);
        var booking = Booking();

        await notifier.ReservationCreatedAsync(booking, CancellationToken.None);
        await notifier.ReservationUpdatedAsync(booking, CancellationToken.None);
        await notifier.ReservationCancelledAsync(booking, CancellationToken.None);
        _push.Queued.Should().BeEmpty();

        await notifier.ReservationStartingSoonAsync(booking, CancellationToken.None);
        await notifier.ReservationReleasedAsync(booking, CancellationToken.None);
        await notifier.ReservationEndingSoonAsync(booking, CancellationToken.None);

        _push.Queued.Select(n => n.Topic).Should().Equal(PushTopic.StartingSoon, PushTopic.Released, PushTopic.EndingSoon);
        _push.Queued.Should().AllSatisfy(n =>
        {
            n.RecipientIds.Should().Equal(booking.UserId);
            n.Reservation.Should().Be(booking);
        });
    }

    [Fact]
    public async Task A_warning_goes_to_everyone_it_warns()
    {
        var booking = Booking();
        Guid[] recipients = [booking.UserId, Guid.NewGuid()];

        await new PushReservationNotifier(_push).ReservationReleaseWarningAsync(booking, recipients, CancellationToken.None);

        _push.Queued.Should().ContainSingle().Which.RecipientIds.Should().Equal(recipients);
    }

    [Fact]
    public async Task Every_new_message_goes_to_its_recipients_devices()
    {
        var messages = new[] { InboxMessageKind.MeetingInvitation, InboxMessageKind.WaitlistBooked }
            .Select(kind => new InboxMessageDto(
                Guid.NewGuid(), Guid.NewGuid(), kind, null, null, "Board Room", Start, Start.AddHours(1), null, null, Start, null))
            .ToList();

        await new PushInboxNotifier(_push).MessagesSentAsync(messages, CancellationToken.None);

        _push.Queued.Should().HaveCount(2);
        _push.Queued.Should().AllSatisfy(n => n.Topic.Should().Be(PushTopic.InboxMessage));
        _push.Queued.Select(n => n.Message).Should().Equal(messages);
        _push.Queued.Select(n => n.RecipientIds.Single()).Should().Equal(messages.Select(m => m.RecipientId));
    }
}
