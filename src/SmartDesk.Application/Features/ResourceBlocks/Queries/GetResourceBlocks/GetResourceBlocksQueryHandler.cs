using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;

public sealed class GetResourceBlocksQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetResourceBlocksQuery, IReadOnlyList<ResourceBlockDto>>
{
    public async Task<IReadOnlyList<ResourceBlockDto>> Handle(GetResourceBlocksQuery request, CancellationToken cancellationToken)
    {
        var query = db.ResourceBlocks
            .AsNoTracking()
            .Where(ResourceBlockOverlap.Overlapping(request.From.AsUtc(), request.To.AsUtc()));

        if (request.ResourceId is { } resourceId)
        {
            query = query.Where(b => b.ResourceId == resourceId);
        }

        return await query
            .OrderBy(b => b.StartDateTime)
            .ThenBy(b => b.Resource.Name)
            .Select(ResourceBlockDto.Projection)
            .ToListAsync(cancellationToken);
    }
}
