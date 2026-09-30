using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;

public sealed class SetResourcePhotoCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    ResourceReader reader,
    TimeProvider timeProvider) : IRequestHandler<SetResourcePhotoCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(SetResourcePhotoCommand request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .UsableBy(currentUser)
            .Include(r => r.Teams)
            .SingleOrDefaultAsync(r => r.Id == request.ResourceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.ResourceId);
        ResourceManagement.RequireManageable(resource, currentUser);

        var photo = await db.ResourcePhotos.SingleOrDefaultAsync(p => p.ResourceId == resource.Id, cancellationToken);
        if (photo is null)
        {
            photo = new ResourcePhoto { ResourceId = resource.Id };
            db.ResourcePhotos.Add(photo);
        }

        photo.ContentType = ResourcePhotoRules.DetectContentType(request.Data)!;
        photo.Data = request.Data;
        photo.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == resource.Id), cancellationToken))!;
    }
}
