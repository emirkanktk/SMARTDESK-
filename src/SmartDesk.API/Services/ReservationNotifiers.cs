using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.API.Services;

// Every reservation event to open screens over SignalR, and what's one person's to their phones and
// browsers as well (ADR-0056). Neither throws (IReservationNotifier), so one never keeps the other
// from going out.
public sealed class ReservationNotifiers(SignalRReservationNotifier live, PushReservationNotifier push) : IReservationNotifier
{
    public async Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationCreatedAsync(reservation, cancellationToken);
        await push.ReservationCreatedAsync(reservation, cancellationToken);
    }

    public async Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationUpdatedAsync(reservation, cancellationToken);
        await push.ReservationUpdatedAsync(reservation, cancellationToken);
    }

    public async Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationCancelledAsync(reservation, cancellationToken);
        await push.ReservationCancelledAsync(reservation, cancellationToken);
    }

    public async Task ReservationStartingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationStartingSoonAsync(reservation, cancellationToken);
        await push.ReservationStartingSoonAsync(reservation, cancellationToken);
    }

    public async Task ReservationReleasedAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationReleasedAsync(reservation, cancellationToken);
        await push.ReservationReleasedAsync(reservation, cancellationToken);
    }

    public async Task ReservationReleaseWarningAsync(
        ReservationDto reservation, IReadOnlyCollection<Guid> recipients, CancellationToken cancellationToken)
    {
        await live.ReservationReleaseWarningAsync(reservation, recipients, cancellationToken);
        await push.ReservationReleaseWarningAsync(reservation, recipients, cancellationToken);
    }

    public async Task ReservationEndingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken)
    {
        await live.ReservationEndingSoonAsync(reservation, cancellationToken);
        await push.ReservationEndingSoonAsync(reservation, cancellationToken);
    }
}
