using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;

// Full replace of what a user can change about a reservation (PUT semantics). The owner
// never changes. Once a reservation has started, only its end and note can change - "the
// meeting runs over" - the start and the resource are fixed by then.
// ReminderMinutes is part of the full replace too: omitting it turns the reminder off, and
// omitting AttendeeIds takes everyone off the list (ADR-0032). Whoever is added is invited,
// whoever is taken off is told, and the rest hear about a new time or room.
public sealed record UpdateReservationCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note,
    int? ReminderMinutes = null,
    IReadOnlyList<Guid>? AttendeeIds = null) : IRequest<ReservationDto>
{
    // Comes from the route (PUT /api/reservations/{id}), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
