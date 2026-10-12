using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.FloorPlans.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.FloorPlans.Queries.GetFloors;

// The floors the caller may see (GET /api/floor-plans): every location of a resource they may
// use (ADR-0022), with its plan when there is one. An Admin also sees plans no resource is on
// yet, to replace or remove them.
public sealed record GetFloorsQuery : IRequest<IReadOnlyList<FloorDto>>;

public sealed class GetFloorsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetFloorsQuery, IReadOnlyList<FloorDto>>
{
    public async Task<IReadOnlyList<FloorDto>> Handle(GetFloorsQuery request, CancellationToken cancellationToken)
    {
        var floors = await db.Resources
            .AsNoTracking()
            .UsableBy(currentUser)
            .GroupBy(r => r.Location)
            .Select(g => new { Location = g.Key, Resources = g.Count(), Placed = g.Count(r => r.MapX != null) })
            .ToListAsync(cancellationToken);

        // Their sizes only, never the images.
        var plans = await db.FloorPlans
            .AsNoTracking()
            .Select(p => new { p.Location, p.Width, p.Height, p.UpdatedAt })
            .ToDictionaryAsync(p => p.Location, p => new FloorPlanImageDto(p.Width, p.Height, p.UpdatedAt), cancellationToken);

        var shown = floors.Select(f => new FloorDto(f.Location, f.Resources, f.Placed, plans.GetValueOrDefault(f.Location)));
        if (currentUser.IsInRole(Roles.Admin))
        {
            var withResources = floors.Select(f => f.Location).ToHashSet();
            shown = shown.Concat(plans
                .Where(p => !withResources.Contains(p.Key))
                .Select(p => new FloorDto(p.Key, 0, 0, p.Value)));
        }

        return shown.OrderBy(f => f.Location, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
