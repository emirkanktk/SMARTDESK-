using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;

public sealed class ChangeResourceStatusCommandHandler(IApplicationDbContext db)
    : IRequestHandler<ChangeResourceStatusCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(ChangeResourceStatusCommand request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.Id);

        // EF only writes the changed column, so a concurrent edit of the name, location or
        // features isn't overwritten. The UPDATE also waits for any booking that holds the
        // resource's row lock (ADR-0011), so a booking in flight either completes first or
        // sees the new status - never a half-and-half state.
        if (resource.Status != request.Status)
        {
            resource.Status = request.Status;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ResourceDto.Projection)
            .SingleAsync(cancellationToken);
    }
}
