using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.RemoveResourcePhoto;

public sealed class RemoveResourcePhotoCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RemoveResourcePhotoCommand>
{
    public async Task Handle(RemoveResourcePhotoCommand request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .UsableBy(currentUser)
            .Include(r => r.Teams)
            .SingleOrDefaultAsync(r => r.Id == request.ResourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.ResourceId);
        ResourceManagement.RequireManageable(resource, currentUser);

        var photo = await db.ResourcePhotos.SingleOrDefaultAsync(p => p.ResourceId == resource.Id, cancellationToken);
        if (photo is not null)
        {
            db.ResourcePhotos.Remove(photo);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
