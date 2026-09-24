using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;

public sealed class GetAvailableResourcesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAvailableResourcesQuery, IReadOnlyList<ResourceDto>>
{
    public async Task<IReadOnlyList<ResourceDto>> Handle(GetAvailableResourcesQuery request, CancellationToken cancellationToken)
    {
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();

        // Business rule 3: only Active resources can be booked, so only they are "available".
        var query = db.Resources.AsNoTracking().Where(r => r.Status == ResourceStatus.Active);

        if (request.ResourceTypeId is { } typeId)
        {
            query = query.Where(r => r.ResourceTypeId == typeId);
        }

        if (request.MinCapacity is { } minCapacity)
        {
            query = query.Where(r => r.Capacity >= minCapacity);
        }

        // Translated to a single NOT IN subquery; uses the (ResourceId, Status, Start, End) index.
        var busyResourceIds = db.Reservations
            .Where(ReservationOverlap.ConfirmedOverlapping(start, end))
            .Select(r => r.ResourceId);

        return await query
            .Where(r => !busyResourceIds.Contains(r.Id))
            .OrderBy(r => r.ResourceType.Name)
            .ThenBy(r => r.Name)
            .Select(ResourceDto.Projection)
            .ToListAsync(cancellationToken);
    }
}
