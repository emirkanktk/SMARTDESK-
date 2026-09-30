using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;

// Sets or replaces a resource's photo (PUT /api/resources/{id}/photo, multipart). Whoever may
// edit the resource may change its photo (ADR-0022).
public sealed record SetResourcePhotoCommand(Guid ResourceId, byte[] Data) : IRequest<ResourceDto>;
