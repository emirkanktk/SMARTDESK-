using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

public sealed class CreateResourceBlockCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreateResourceBlockCommand, ResourceBlockDto>
{
    public async Task<ResourceBlockDto> Handle(CreateResourceBlockCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var blockId = Guid.CreateVersion7();

        // Takes the same per-resource lock as booking (ADR-0011), so a block and a reservation
        // can't both claim the same time: whichever commits first, the other sees it.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            // An Admin blocks anything; a Manager only what belongs to their team alone (ADR-0022).
            var resource = await db.Resources
                .UsableBy(currentUser)
                .Include(r => r.Teams)
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == request.ResourceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.ResourceId);
            ResourceManagement.RequireManageable(resource, currentUser);
            var resourceName = resource.Name;

            var alreadyBlocked = await db.ResourceBlocks
                .Where(b => b.ResourceId == request.ResourceId)
                .AnyAsync(ResourceBlockOverlap.Overlapping(start, end), cancellationToken);
            if (alreadyBlocked)
            {
                throw new ConflictException(string.Format(Messages.Blocks_OverlapsBlock, resourceName))
                {
                    Code = ConflictCodes.BlockOverlapsBlock,
                };
            }

            // Existing bookings are not cancelled behind anyone's back: the admin decides what
            // happens to them first (cancel or move - both are existing endpoints), then blocks.
            var clashing = await db.Reservations
                .Where(r => r.ResourceId == request.ResourceId)
                .CountAsync(ReservationOverlap.ConfirmedOverlapping(start, end), cancellationToken);
            if (clashing > 0)
            {
                throw new ConflictException(string.Format(Messages.Blocks_OverlapsReservations, clashing, resourceName))
                {
                    Code = ConflictCodes.BlockOverlapsReservations,
                };
            }

            db.ResourceBlocks.Add(new ResourceBlock
            {
                Id = blockId,
                ResourceId = request.ResourceId,
                StartDateTime = start,
                EndDateTime = end,
                Reason = request.Reason.Trim(),
                CreatedByUserId = userId,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.Id == blockId)
            .Select(ResourceBlockDto.Projection)
            .SingleAsync(cancellationToken);
    }
}
