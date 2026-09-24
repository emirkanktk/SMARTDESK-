using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Queries.GetResourceById;

public sealed class GetResourceByIdQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetResourceByIdQuery, ResourceDto>
{
    public async Task<ResourceDto> Handle(GetResourceByIdQuery request, CancellationToken cancellationToken) =>
        await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ResourceDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Resource), request.Id);
}
