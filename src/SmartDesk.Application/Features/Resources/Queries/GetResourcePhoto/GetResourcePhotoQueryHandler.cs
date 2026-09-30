using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Queries.GetResourcePhoto;

public sealed class GetResourcePhotoQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetResourcePhotoQuery, ResourcePhotoFile>
{
    public async Task<ResourcePhotoFile> Handle(GetResourcePhotoQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Resources.UsableBy(currentUser).AnyAsync(r => r.Id == request.ResourceId, cancellationToken))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        return await db.ResourcePhotos
            .AsNoTracking()
            .Where(p => p.ResourceId == request.ResourceId)
            .Select(p => new ResourcePhotoFile(p.ContentType, p.Data, p.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(ResourcePhoto), request.ResourceId);
    }
}
