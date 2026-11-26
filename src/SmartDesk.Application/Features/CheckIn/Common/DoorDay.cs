using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.CheckIn.Common;

// The rest of a resource's day as its door shows it, to its screen (ADR-0034) and to whoever opens
// its page without an account (ADR-0050): the bookings that aren't over, as times only (a corridor
// is no place for names), and its blocked windows, earliest first. A lobby screen shows its rooms'
// the same way (ADR-0064).
public static class DoorDay
{
    public static async Task<(IReadOnlyList<DoorBookingDto> Bookings, IReadOnlyList<DoorBlockDto> Blocks)> LoadAsync(
        IApplicationDbContext db, Guid resourceId, DateTime now, DateTime dayEnd, int maxBookings, CancellationToken cancellationToken)
    {
        var days = await LoadManyAsync(db, [resourceId], now, dayEnd, maxBookings, cancellationToken);
        return days[resourceId];
    }

    // The same for several resources at once, in two queries; each id is in the answer, with
    // nothing left of the day when that's so.
    public static async Task<IReadOnlyDictionary<Guid, (IReadOnlyList<DoorBookingDto> Bookings, IReadOnlyList<DoorBlockDto> Blocks)>> LoadManyAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> resourceIds,
        DateTime now,
        DateTime dayEnd,
        int maxBookingsEach,
        CancellationToken cancellationToken)
    {
        var bookings = await db.Reservations
            .AsNoTracking()
            // Waiting for approval holds the time too (ADR-0049), with nothing to check in yet.
            .Where(r => resourceIds.Contains(r.ResourceId)
                && r.Status != ReservationStatus.Cancelled
                && r.EndDateTime > now
                && r.StartDateTime < dayEnd)
            .OrderBy(r => r.StartDateTime)
            .Select(r => new
            {
                r.ResourceId,
                Booking = new DoorBookingDto(
                    r.StartDateTime,
                    r.EndDateTime,
                    r.Status == ReservationStatus.Confirmed && r.CheckInRequired && r.Resource.CheckInRequired,
                    r.CheckedInAt != null),
            })
            .ToListAsync(cancellationToken);
        var blocks = await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => resourceIds.Contains(b.ResourceId) && b.EndDateTime > now && b.StartDateTime < dayEnd)
            .OrderBy(b => b.StartDateTime)
            .Select(b => new { b.ResourceId, Block = new DoorBlockDto(b.StartDateTime, b.EndDateTime, b.Reason) })
            .ToListAsync(cancellationToken);

        return resourceIds.Distinct().ToDictionary(
            id => id,
            id => (
                (IReadOnlyList<DoorBookingDto>)bookings.Where(b => b.ResourceId == id).Select(b => b.Booking).Take(maxBookingsEach).ToList(),
                (IReadOnlyList<DoorBlockDto>)blocks.Where(b => b.ResourceId == id).Select(b => b.Block).ToList()));
    }
}
