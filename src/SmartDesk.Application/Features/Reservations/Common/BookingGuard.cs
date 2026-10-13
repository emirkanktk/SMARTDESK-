using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Common;

// The checks a [start, end) claim on a resource must pass, shared by creating a reservation,
// moving one and booking a series (ADR-0035), so they can't drift apart. Call it inside the
// transaction, after LockResourceForBookingAsync, so what it reads can't change before the
// write (ADR-0011). What the booking is for - how long, how far ahead, within office hours, how
// many one person has - is BookingPolicy's to say (ADR-0046), between these checks: once the
// resource is known to be the caller's to book, before whether it's free.
public static class BookingGuard
{
    // Returns what the booking takes on from the resource: its name for messages, and whether
    // it asks for a check-in, which the booking keeps (ADR-0030). `ignoreReservationId` is the
    // reservation being moved: its own current slot must not count as a clash.
    public static async Task<BookableResource> EnsureBookableAsync(
        IApplicationDbContext db,
        ICurrentUserService user,
        Guid resourceId,
        DateTime start,
        DateTime end,
        Guid? ignoreReservationId,
        CancellationToken cancellationToken)
    {
        var resource = await EnsureResourceBookableAsync(db, user, resourceId, cancellationToken);
        await EnsureFreeAsync(db, resourceId, resource.Name, start, end, ignoreReservationId, cancellationToken);
        return resource;
    }

    // Nothing else holds any of [start, end): no blocked window, no other booking.
    public static async Task EnsureFreeAsync(
        IApplicationDbContext db,
        Guid resourceId,
        string resourceName,
        DateTime start,
        DateTime end,
        Guid? ignoreReservationId,
        CancellationToken cancellationToken)
    {
        var conflicts = await FindConflictsAsync(db, resourceId, resourceName, [new Occurrence(start, end)], ignoreReservationId, cancellationToken);
        if (conflicts[0] is { } conflict)
        {
            throw new ConflictException(conflict.Reason) { Code = conflict.Code };
        }
    }

    // The resource can be booked at all: the caller may use it, and it's Active.
    public static async Task<BookableResource> EnsureResourceBookableAsync(
        IApplicationDbContext db,
        ICurrentUserService user,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        // Another team's resource can't be booked, and isn't even there for the caller (ADR-0022).
        var resource = await db.Resources
            .AsNoTracking()
            .UsableBy(user)
            .Where(r => r.Id == resourceId)
            .Select(r => new { r.Name, r.Status, r.CheckInRequired })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), resourceId);

        // Business rule 3.
        if (resource.Status != ResourceStatus.Active)
        {
            throw new ConflictException(string.Format(Messages.Booking_NotBookable, resource.Name, Languages.Describe(resource.Status)))
            {
                Code = ConflictCodes.ResourceUnavailable,
            };
        }

        return new BookableResource(resource.Name, resource.CheckInRequired);
    }

    // For each slot, why it can't be booked, or null when it can: a blocked window over part
    // of it (business rule 7, ADR-0015) - the earliest one's reason - or another Confirmed
    // booking (business rule 1). Two queries whatever the number of slots.
    public static async Task<IReadOnlyList<SlotConflict?>> FindConflictsAsync(
        IApplicationDbContext db,
        Guid resourceId,
        string resourceName,
        IReadOnlyList<Occurrence> slots,
        Guid? ignoreReservationId,
        CancellationToken cancellationToken)
    {
        if (slots.Count == 0)
        {
            return [];
        }

        var from = slots.Min(s => s.Start);
        var to = slots.Max(s => s.End);
        var blocks = await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.ResourceId == resourceId)
            .Where(ResourceBlockOverlap.Overlapping(from, to))
            .OrderBy(b => b.StartDateTime)
            .Select(b => new { b.StartDateTime, b.EndDateTime, b.Reason })
            .ToListAsync(cancellationToken);
        var taken = await db.Reservations
            .AsNoTracking()
            .Where(r => r.ResourceId == resourceId && r.Id != ignoreReservationId)
            .Where(ReservationOverlap.ConfirmedOverlapping(from, to))
            .Select(r => new { r.StartDateTime, r.EndDateTime })
            .ToListAsync(cancellationToken);

        return slots
            .Select(slot =>
            {
                var block = blocks.FirstOrDefault(b => ReservationOverlap.Overlaps(b.StartDateTime, b.EndDateTime, slot.Start, slot.End));
                if (block is not null)
                {
                    return new SlotConflict(ConflictCodes.ResourceBlocked, string.Format(Messages.Booking_Blocked, resourceName, block.Reason));
                }

                return taken.Any(r => ReservationOverlap.Overlaps(r.StartDateTime, r.EndDateTime, slot.Start, slot.End))
                    ? new SlotConflict(ConflictCodes.ReservationOverlaps, string.Format(Messages.Booking_Taken, resourceName))
                    : null;
            })
            .ToList();
    }
}

public sealed record BookableResource(string Name, bool CheckInRequired);

// Why a slot can't be booked: the ConflictCodes value, and the message to show.
public sealed record SlotConflict(string Code, string Reason);
