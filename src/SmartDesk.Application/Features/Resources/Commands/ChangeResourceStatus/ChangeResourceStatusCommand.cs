using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;

// Changes only the status (PATCH /api/resources/{id}/status). Activate/Deactivate needs
// nothing else, and sending the full resource through PUT would overwrite whatever another
// admin changed in between. As with PUT, existing reservations are kept.
public sealed record ChangeResourceStatusCommand(ResourceStatus Status) : IRequest<ResourceDto>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
