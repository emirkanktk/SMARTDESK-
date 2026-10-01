using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;

// The caller's answer to a meeting they're invited to (ADR-0032): coming, or not and why. The
// organizer gets a message either way; the reason goes to them only.
public sealed record RespondToInvitationCommand(AttendeeResponse Response, string? Reason = null) : IRequest<ReservationDto>
{
    // Comes from the route (POST /api/reservations/{id}/response), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
