using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.FloorPlans.Commands.RemoveFloorPlanImage;

// Removes a floor's plan (DELETE /api/floor-plans/image?location=); Admins only. Its resources
// keep their places, ready for the next plan. Removing a plan that isn't there is fine.
public sealed record RemoveFloorPlanImageCommand(string Location) : IRequest;

public sealed class RemoveFloorPlanImageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RemoveFloorPlanImageCommand>
{
    public async Task Handle(RemoveFloorPlanImageCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenAccessException(Messages.FloorPlans_AdminsOnly);
        }

        var location = request.Location.Trim();
        var plan = await db.FloorPlans.SingleOrDefaultAsync(p => p.Location == location, cancellationToken);
        if (plan is not null)
        {
            db.FloorPlans.Remove(plan);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
