using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Common.Interfaces;

// Tells open screens that a reservation changed, so calendars and dashboards update without
// a reload (docs/API_CONTRACTS.md, Real-time). Handlers call it only after the change is
// committed. The SignalR implementation belongs to the integration area; until it's plugged
// in, a no-op implementation is registered.
//
// Implementations must not throw: the reservation is already saved, and a failed broadcast
// must not turn a successful request into an error.
public interface IReservationNotifier
{
    Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken);

    // Carries the reservation as it is now; its resource or times may differ from before, so
    // screens should replace the entry by id rather than patch it in place.
    Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken);

    Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken);

    // The owner's reminder (ADR-0018): sent to that user's open sessions only, not broadcast.
    Task ReservationStartingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken);

    // Tells the owner, and only them, that their booking was released because nobody checked
    // in (ADR-0030). The release itself reaches everyone as ReservationCancelledAsync.
    Task ReservationReleasedAsync(ReservationDto reservation, CancellationToken cancellationToken);

    // Warns the owner and those invited, and only them, that nobody has checked in yet and the
    // booking is about to be released (ADR-0034).
    Task ReservationReleaseWarningAsync(
        ReservationDto reservation, IReadOnlyCollection<Guid> recipients, CancellationToken cancellationToken);

    // Asks the owner, and only them, whether their booking that's about to end needs longer
    // (ADR-0033).
    Task ReservationEndingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken);
}
