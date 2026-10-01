using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;

// The caller's answer to a meeting they're invited to (ADR-0032): coming, or not and why. The
// organizer gets a message either way; the reason goes to them only. Following: for a meeting
// that repeats, the answer is for this date and every later one they're invited to, with one
// message for them all (ADR-0035).
public sealed record RespondToInvitationCommand(AttendeeResponse Response, string? Reason = null, bool Following = false)
    : IRequest<ReservationDto>
{
    // Comes from the route (POST /api/reservations/{id}/response), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
