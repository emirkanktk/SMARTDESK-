using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.CheckIn.Common;

// The rest of a resource's day as its door shows it, to its screen (ADR-0034) and to whoever opens
// its page without an account (ADR-0050): the bookings that aren't over, as times only (a corridor
// is no place for names), and its blocked windows, earliest first.
public static class DoorDay
{
    public static async Task<(IReadOnlyList<DoorBookingDto> Bookings, IReadOnlyList<DoorBlockDto> Blocks)> LoadAsync(
        IApplicationDbContext db, Guid resourceId, DateTime now, DateTime dayEnd, int maxBookings, CancellationToken cancellationToken)
    {
        var bookings = await db.Reservations
            .AsNoTracking()
            // Waiting for approval holds the time too (ADR-0049), with nothing to check in yet.
            .Where(r => r.ResourceId == resourceId
                && r.Status != ReservationStatus.Cancelled
                && r.EndDateTime > now
                && r.StartDateTime < dayEnd)
            .OrderBy(r => r.StartDateTime)
            .Take(maxBookings)
            .Select(r => new DoorBookingDto(
                r.StartDateTime,
                r.EndDateTime,
                r.Status == ReservationStatus.Confirmed && r.CheckInRequired && r.Resource.CheckInRequired,
                r.CheckedInAt != null))
            .ToListAsync(cancellationToken);
        var blocks = await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.ResourceId == resourceId && b.EndDateTime > now && b.StartDateTime < dayEnd)
            .OrderBy(b => b.StartDateTime)
            .Select(b => new DoorBlockDto(b.StartDateTime, b.EndDateTime, b.Reason))
            .ToListAsync(cancellationToken);
        return (bookings, blocks);
    }
}
