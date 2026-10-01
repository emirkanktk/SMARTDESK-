using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.CheckIn.Common;

// A resource's QR code is for the people who may edit the resource: an Admin, or a Manager
// for a resource of their team alone (ADR-0022). Anyone else could print it and check in
// without ever going to the door.
internal static class CheckInCodes
{
    // Tracked, so the caller can replace the code and save.
    public static async Task<Resource> FindManageableAsync(
        IApplicationDbContext db, ICurrentUserService user, Guid resourceId, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .UsableBy(user)
            .Include(r => r.Teams)
            .SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), resourceId);
        ResourceManagement.RequireManageable(resource, user);
        return resource;
    }

    public static CheckInCodeDto ToDto(Resource resource, IFrontendLinks links) =>
        new(resource.CheckInCode, links.CheckIn(resource.Id, resource.CheckInCode), resource.DoorScreenSetUpAt);
}
