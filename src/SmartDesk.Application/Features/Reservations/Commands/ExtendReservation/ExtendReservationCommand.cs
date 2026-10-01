using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;

// Lets a booking run longer by one of ExtensionRules.Options (ADR-0033), for the owner or an
// Admin, as long as the resource is free for the extra time. Only the end moves: it's the same
// visit, so the check-in, the reminder and the answers to the invitation stay as they are.
public sealed record ExtendReservationCommand(int Minutes) : IRequest<ReservationDto>
{
    // Comes from the route (POST /api/reservations/{id}/extend), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
