using MediatR;

namespace SmartDesk.Application.Features.ResourceTypes.Queries.GetResourceTypes;

public sealed record GetResourceTypesQuery : IRequest<IReadOnlyList<ResourceTypeDto>>;

public sealed record ResourceTypeDto(Guid Id, string Name);
