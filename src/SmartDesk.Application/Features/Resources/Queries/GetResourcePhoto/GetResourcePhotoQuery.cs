using MediatR;

namespace SmartDesk.Application.Features.Resources.Queries.GetResourcePhoto;

// The photo of a resource the caller may use (GET /api/resources/{id}/photo). Another team's
// resource is 404, as it is everywhere else (ADR-0022).
public sealed record GetResourcePhotoQuery(Guid ResourceId) : IRequest<ResourcePhotoFile>;

public sealed record ResourcePhotoFile(string ContentType, byte[] Data, DateTime UpdatedAt);
