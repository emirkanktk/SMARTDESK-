using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourceTeams;

// Admin only (PATCH /api/resources/{id}/teams). Replaces the teams a resource belongs to; an
// empty list makes it shared by everyone (ADR-0022). Existing reservations are kept, as with a
// status change: whoever booked it keeps their booking, and new ones follow the new teams.
public sealed record SetResourceTeamsCommand(IReadOnlyList<Guid> TeamIds) : IRequest<ResourceDto>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
