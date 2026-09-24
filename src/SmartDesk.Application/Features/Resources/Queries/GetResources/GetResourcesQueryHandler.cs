using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Queries.GetResources;

public sealed class GetResourcesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetResourcesQuery, IReadOnlyList<ResourceDto>>
{
    public async Task<IReadOnlyList<ResourceDto>> Handle(GetResourcesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Resources.AsNoTracking();

        if (request.ResourceTypeId is { } typeId)
        {
            query = query.Where(r => r.ResourceTypeId == typeId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query
            .OrderBy(r => r.ResourceType.Name)
            .ThenBy(r => r.Name)
            .Select(ResourceDto.Projection)
            .ToListAsync(cancellationToken);
    }
}
