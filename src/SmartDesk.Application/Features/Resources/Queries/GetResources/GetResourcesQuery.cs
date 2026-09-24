using MediatR;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Queries.GetResources;

// Every resource regardless of status unless filtered - the Resources screen shows inactive
// and under-maintenance ones too (greyed out), and admins need them to re-activate.
public sealed record GetResourcesQuery(Guid? ResourceTypeId = null, ResourceStatus? Status = null)
    : IRequest<IReadOnlyList<ResourceDto>>;
