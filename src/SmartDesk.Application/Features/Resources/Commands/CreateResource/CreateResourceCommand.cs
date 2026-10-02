using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

// New resources always start Active; status changes go through UpdateResourceCommand.
// TeamIds (ADR-0022): an Admin chooses the teams, none meaning shared by everyone; a Manager's
// new resource always belongs to their own team only. CheckInRequired (ADR-0030): bookings of
// it must be checked in at the door.
public sealed record CreateResourceCommand(
    string Name,
    Guid ResourceTypeId,
    int Capacity,
    string Location,
    string? Description,
    IReadOnlyList<string>? Features,
    IReadOnlyList<Guid>? TeamIds = null,
    bool CheckInRequired = false) : IRequest<ResourceDto>, IResourceFields;
