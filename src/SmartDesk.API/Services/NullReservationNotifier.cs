using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.API.Services;

// Placeholder until the SignalR implementation (integration area) replaces it: reservations
// work fully, open screens just don't get live updates yet. Registered with TryAdd, so
// registering the real notifier before it in Program.cs is enough to swap it out.
public sealed class NullReservationNotifier : IReservationNotifier
{
    public Task ReservationCreatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationUpdatedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationCancelledAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationStartingSoonAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReservationReleasedAsync(ReservationDto reservation, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
