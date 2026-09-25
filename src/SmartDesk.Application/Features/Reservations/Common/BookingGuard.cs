using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Common;

// The checks a [start, end) claim on a resource must pass, shared by creating a reservation
// and moving one, so the two can't drift apart. Call it inside the transaction, after
// LockResourceForBookingAsync, so what it reads can't change before the write (ADR-0011).
public static class BookingGuard
{
    // Returns the resource name for messages. `ignoreReservationId` is the reservation being
    // moved: its own current slot must not count as a clash.
    public static async Task<string> EnsureBookableAsync(
        IApplicationDbContext db,
        Guid resourceId,
        DateTime start,
        DateTime end,
        Guid? ignoreReservationId,
        CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == resourceId)
            .Select(r => new { r.Name, r.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), resourceId);

        // Business rule 3.
        if (resource.Status != ResourceStatus.Active)
        {
            throw new ConflictException($"{resource.Name} can't be reserved right now (status: {resource.Status}).");
        }

        // Business rule 7: blocked windows (ADR-0015).
        var blockReason = await db.ResourceBlocks
            .Where(b => b.ResourceId == resourceId)
            .Where(ResourceBlockOverlap.Overlapping(start, end))
            .OrderBy(b => b.StartDateTime)
            .Select(b => b.Reason)
            .FirstOrDefaultAsync(cancellationToken);
        if (blockReason is not null)
        {
            throw new ConflictException($"{resource.Name} is blocked for part of that time: {blockReason}");
        }

        // Business rule 1.
        var overlaps = await db.Reservations
            .Where(r => r.ResourceId == resourceId && r.Id != ignoreReservationId)
            .AnyAsync(ReservationOverlap.ConfirmedOverlapping(start, end), cancellationToken);
        if (overlaps)
        {
            throw new ConflictException($"{resource.Name} is already reserved for part of that time.");
        }

        return resource.Name;
    }
}
