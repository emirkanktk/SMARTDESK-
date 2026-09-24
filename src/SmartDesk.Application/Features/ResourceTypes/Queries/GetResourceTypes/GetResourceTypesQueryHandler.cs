using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.ResourceTypes.Queries.GetResourceTypes;

public sealed class GetResourceTypesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetResourceTypesQuery, IReadOnlyList<ResourceTypeDto>>
{
    public async Task<IReadOnlyList<ResourceTypeDto>> Handle(GetResourceTypesQuery request, CancellationToken cancellationToken) =>
        await db.ResourceTypes
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new ResourceTypeDto(t.Id, t.Name))
            .ToListAsync(cancellationToken);
}
