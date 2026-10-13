using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;

public sealed class GetAvailableResourcesQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ResourceReader reader,
    BookingPolicy policy)
    : IRequestHandler<GetAvailableResourcesQuery, IReadOnlyList<ResourceDto>>
{
    public async Task<IReadOnlyList<ResourceDto>> Handle(GetAvailableResourcesQuery request, CancellationToken cancellationToken)
    {
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();

        // Business rule 3: only Active resources can be booked, so only they are "available".
        // And only what the caller may book (ADR-0022).
        var query = db.Resources.AsNoTracking().UsableBy(currentUser).Where(r => r.Status == ResourceStatus.Active);

        // And only kinds whose rules let the window be booked (ADR-0046): a desk kept to office
        // hours isn't available at 20:00.
        var kinds = await policy.KindsBookableAsync(start, end, cancellationToken);
        query = query.Where(r => kinds.Contains(r.ResourceTypeId));

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

        // Business rule 7: a resource blocked for any part of the window isn't available.
        var blockedResourceIds = db.ResourceBlocks
            .Where(ResourceBlockOverlap.Overlapping(start, end))
            .Select(b => b.ResourceId);

        return await reader.ListAsync(
            query
                .Where(r => !busyResourceIds.Contains(r.Id) && !blockedResourceIds.Contains(r.Id))
                .OrderBy(r => r.ResourceType.Name)
                .ThenBy(r => r.Name),
            cancellationToken);
    }
}
