using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;

// Full replace of what a user can change about a reservation (PUT semantics). The owner
// never changes. Once a reservation has started, only its end and note can change - "the
// meeting runs over" - the start and the resource are fixed by then.
// ReminderMinutes is part of the full replace too: omitting it turns the reminder off.
public sealed record UpdateReservationCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note,
    int? ReminderMinutes = null) : IRequest<ReservationDto>
{
    // Comes from the route (PUT /api/reservations/{id}), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
