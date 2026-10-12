using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.FloorPlans.Queries.GetFloorPlanImage;

// A floor's plan (GET /api/floor-plans/image?location=), for anyone who may use a resource on
// that floor, and for Admins. Anyone else gets the same 404 as for no plan, as for another
// team's resource (ADR-0022).
public sealed record GetFloorPlanImageQuery(string Location) : IRequest<FloorPlanFile>;

public sealed record FloorPlanFile(string ContentType, byte[] Data, DateTime UpdatedAt);

public sealed class GetFloorPlanImageQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetFloorPlanImageQuery, FloorPlanFile>
{
    public async Task<FloorPlanFile> Handle(GetFloorPlanImageQuery request, CancellationToken cancellationToken)
    {
        var location = request.Location.Trim();
        var mayView = currentUser.IsInRole(Roles.Admin)
            || await db.Resources.UsableBy(currentUser).AnyAsync(r => r.Location == location, cancellationToken);
        if (!mayView)
        {
            throw new NotFoundException(nameof(FloorPlan), location);
        }

        return await db.FloorPlans
            .AsNoTracking()
            .Where(p => p.Location == location)
            .Select(p => new FloorPlanFile(p.ContentType, p.Data, p.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(FloorPlan), location);
    }
}
