using MediatR;

namespace SmartDesk.Application.Features.Resources.Commands.RemoveResourcePhoto;

// Removes a resource's photo (DELETE /api/resources/{id}/photo); its type's picture shows
// instead. Removing one that isn't there is fine.
public sealed record RemoveResourcePhotoCommand(Guid ResourceId) : IRequest;
