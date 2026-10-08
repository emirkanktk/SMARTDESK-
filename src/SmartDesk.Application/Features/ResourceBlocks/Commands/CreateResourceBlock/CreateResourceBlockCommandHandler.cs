using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

public sealed class CreateResourceBlockCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations inbox) : IRequestHandler<CreateResourceBlockCommand, ResourceBlockDto>
{
    // A booking made in the window between looking for bookings and locking the resource
    // means looking again; this many times, then giving up with the usual 409.
    private const int Attempts = 3;

    public async Task<ResourceBlockDto> Handle(CreateResourceBlockCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();

        for (var attempt = 1; ; attempt++)
        {
            var created = await TryCreateAsync(request, userId, start, end, lastAttempt: attempt == Attempts, cancellationToken);
            if (created is not null)
            {
                return created;
            }
        }
    }

    // Null when a booking slipped into the window before the resource was locked: then the
    // caller looks again.
    private async Task<ResourceBlockDto?> TryCreateAsync(
        CreateResourceBlockCommand request, Guid userId, DateTime start, DateTime end, bool lastAttempt, CancellationToken cancellationToken)
    {
        var blockId = Guid.CreateVersion7();

        // The bookings to cancel, locked one by one before the resource, as every handler takes
        // them (IApplicationDbContext): the other way round, a booking being moved at the same
        // moment could wait on the resource while this waits on the booking.
        var toLock = request.CancelReservations
            ? await db.Reservations
                .AsNoTracking()
                .Where(r => r.ResourceId == request.ResourceId)
                .Where(ReservationOverlap.ConfirmedOverlapping(start, end))
                .Select(r => r.Id)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken)
            : [];

        List<Reservation> cancelled;
        var messages = new List<InboxMessage>();

        // Takes the same per-resource lock as booking (ADR-0011), so a block and a reservation
        // can't both claim the same time: whichever commits first, the other sees it.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            foreach (var id in toLock)
            {
                await db.LockReservationAsync(id, cancellationToken);
            }

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

            var clashing = await db.Reservations
                .Where(r => r.ResourceId == request.ResourceId)
                .Where(ReservationOverlap.ConfirmedOverlapping(start, end))
                .ToListAsync(cancellationToken);

            // Booked after the look, before the lock, so not locked here.
            var bookedMeanwhile = clashing.Any(r => !toLock.Contains(r.Id));

            // Without being asked to, existing bookings are not cancelled behind anyone's back:
            // the admin decides what happens to them first (ADR-0015). Asked to, but bookings
            // keep arriving: the same answer, rather than looking forever.
            if (clashing.Count > 0 && (!request.CancelReservations || (bookedMeanwhile && lastAttempt)))
            {
                throw new ConflictException(string.Format(Messages.Blocks_OverlapsReservations, clashing.Count, resourceName))
                {
                    Code = ConflictCodes.BlockOverlapsReservations,
                };
            }

            if (bookedMeanwhile)
            {
                return null;
            }

            // Asked to (ADR-0038): they go with the block, and everyone on them is told why.
            foreach (var reservation in clashing)
            {
                reservation.Status = ReservationStatus.Cancelled;
                messages.AddRange(await CancellationMessagesAsync(reservation, resourceName, userId, request.Reason.Trim(), cancellationToken));
            }

            cancelled = clashing;

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

        // Committed: every screen frees the slots, and the people on them get their message.
        if (cancelled.Count > 0)
        {
            var ids = cancelled.Select(r => r.Id).ToList();
            var dtos = await identityService.WithUserNamesAsync(
                await db.Reservations
                    .AsNoTracking()
                    .Where(r => ids.Contains(r.Id))
                    .Select(ReservationDto.Projection)
                    .ToListAsync(cancellationToken),
                cancellationToken);
            foreach (var dto in dtos)
            {
                await notifier.ReservationCancelledAsync(dto, cancellationToken);
            }

            await inbox.SendAsync(messages, cancellationToken);
        }

        return await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.Id == blockId)
            .Select(ResourceBlockDto.Projection)
            .SingleAsync(cancellationToken);
    }

    // To the booking's owner and everyone invited, with the block's reason, from whoever
    // blocked it - unless that's them.
    private async Task<IReadOnlyList<InboxMessage>> CancellationMessagesAsync(
        Reservation reservation, string resourceName, Guid blockedBy, string reason, CancellationToken cancellationToken)
    {
        var attendees = await db.ReservationAttendees
            .Where(a => a.ReservationId == reservation.Id)
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken);
        var recipients = attendees.Prepend(reservation.UserId).Distinct().Where(id => id != blockedBy).ToList();
        return recipients.Count == 0
            ? []
            : inbox.Write(InboxMessageKind.CancelledByBlock, recipients, reservation, resourceName, blockedBy, reason);
    }
}
