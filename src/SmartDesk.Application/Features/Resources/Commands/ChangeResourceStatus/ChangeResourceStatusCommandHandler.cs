using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;

public sealed class ChangeResourceStatusCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, ResourceReader reader, TimeProvider timeProvider)
    : IRequestHandler<ChangeResourceStatusCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(ChangeResourceStatusCommand request, CancellationToken cancellationToken)
    {
        // The booking lock first (ADR-0011): a booking in flight completes before the count,
        // and none can start until the new status is saved.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.Id, cancellationToken);

            var resource = await db.Resources
                .UsableBy(currentUser)
                .Include(r => r.Teams)
                .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.Id);
            ResourceManagement.RequireManageable(resource, currentUser);
            await ResourceManagement.RequireNoUpcomingReservationsAsync(
                db, resource, request.Status, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            // EF only writes the changed column, so a concurrent edit of the name, location or
            // features isn't overwritten.
            if (resource.Status != request.Status)
            {
                resource.Status = request.Status;
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == request.Id), cancellationToken))!;
    }
}
