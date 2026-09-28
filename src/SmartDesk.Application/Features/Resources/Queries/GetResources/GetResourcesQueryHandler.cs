using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Queries.GetResources;

public sealed class GetResourcesQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, ResourceReader reader)
    : IRequestHandler<GetResourcesQuery, IReadOnlyList<ResourceDto>>
{
    public async Task<IReadOnlyList<ResourceDto>> Handle(GetResourcesQuery request, CancellationToken cancellationToken)
    {
        // Only what the caller may use (ADR-0022).
        var query = db.Resources.AsNoTracking().UsableBy(currentUser);

        if (request.ResourceTypeId is { } typeId)
        {
            query = query.Where(r => r.ResourceTypeId == typeId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        return await reader.ListAsync(
            query.OrderBy(r => r.ResourceType.Name).ThenBy(r => r.Name),
            cancellationToken);
    }
}
