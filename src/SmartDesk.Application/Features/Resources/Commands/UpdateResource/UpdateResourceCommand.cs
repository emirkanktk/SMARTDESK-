using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Commands.UpdateResource;

// Full replace of the mutable fields (PUT semantics): omitted features means "no features",
// and an omitted CheckInRequired means check-in is off.
// Setting Status to Inactive/Maintenance stops new bookings; existing reservations are kept.
// Switching check-in on or off applies as ADR-0030 describes: bookings already made don't
// start needing a check-in, and none is released while it's off.
public sealed record UpdateResourceCommand(
    string Name,
    Guid ResourceTypeId,
    int Capacity,
    string Location,
    string? Description,
    IReadOnlyList<string>? Features,
    ResourceStatus Status,
    bool CheckInRequired = false) : IRequest<ResourceDto>, IResourceFields
{
    // Comes from the route (PUT /api/resources/{id}), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
