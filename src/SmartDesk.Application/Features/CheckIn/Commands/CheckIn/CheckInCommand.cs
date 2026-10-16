using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.CheckIn.Commands.CheckIn;

// Checks the caller in to their booking of the resource whose window is open now (ADR-0030).
// Code is the secret from the resource's QR code, which is how the page at the door got here.
public sealed record CheckInCommand(string Code) : IRequest<ReservationDto>
{
    // Comes from the route (POST /api/resources/{id}/check-in), not the body.
    [JsonIgnore]
    public Guid ResourceId { get; init; }

    // From the door's own endpoint (POST /api/door/{id}/check-in, ADR-0050): whoever stands
    // there, with or without an account, checks in the booking waiting for it, whoever's it is.
    // Set by the endpoint, never by a body.
    [JsonIgnore]
    public bool AtTheDoor { get; init; }
}
