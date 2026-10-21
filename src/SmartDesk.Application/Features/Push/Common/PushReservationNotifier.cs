using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Push.Common;

// Puts on people's devices (ADR-0056) what reaches one person about their booking: the reminder,
// the warning before a release, the release, and the question before it ends. The moment matters
// for these, and the app may well be closed. Bookings made, changed or cancelled go to open screens
// only; the messages they send people (ADR-0032) reach devices through PushInboxNotifier.
public sealed class PushReservationNotifier(IPushSender push) : IReservationNotifier
{
    public Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationStartingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Queue(PushTopic.StartingSoon, reservation, [reservation.UserId]);

    public Task ReservationReleasedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Queue(PushTopic.Released, reservation, [reservation.UserId]);

    public Task ReservationReleaseWarningAsync(
        ReservationDto reservation, IReadOnlyCollection<Guid> recipients, CancellationToken cancellationToken) =>
        Queue(PushTopic.ReleaseWarning, reservation, recipients);

    public Task ReservationEndingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Queue(PushTopic.EndingSoon, reservation, [reservation.UserId]);

    private Task Queue(PushTopic topic, ReservationDto reservation, IReadOnlyCollection<Guid> recipients)
    {
        push.Queue(new PushNotice(topic, recipients) { Reservation = reservation });
        return Task.CompletedTask;
    }
}
