using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Commands.UpdateResource;

// Full replace of the mutable fields (PUT semantics): omitted features means "no features".
// Setting Status to Inactive/Maintenance stops new bookings; existing reservations are kept.
public sealed record UpdateResourceCommand(
    string Name,
    Guid ResourceTypeId,
    int Capacity,
    string Location,
    string? Description,
    IReadOnlyList<string>? Features,
    ResourceStatus Status) : IRequest<ResourceDto>, IResourceFields
{
    // Comes from the route (PUT /api/resources/{id}), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
