using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Queries.GetResourceById;

public sealed class GetResourceByIdQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, ResourceReader reader)
    : IRequestHandler<GetResourceByIdQuery, ResourceDto>
{
    // Another team's resource is "not found", like one that doesn't exist (ADR-0022).
    public async Task<ResourceDto> Handle(GetResourceByIdQuery request, CancellationToken cancellationToken) =>
        await reader.FindAsync(
            db.Resources.AsNoTracking().UsableBy(currentUser).Where(r => r.Id == request.Id),
            cancellationToken)
        ?? throw new NotFoundException(nameof(Resource), request.Id);
}
