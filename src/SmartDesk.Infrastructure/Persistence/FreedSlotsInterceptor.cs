using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartDesk.Application.Features.Waitlist.Offers;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Persistence;

// Notes the time a save gives back on a resource, for its waiting list (ADR-0043): a Confirmed
// booking cancelled - or released for want of a check-in - its start moved later or its end
// earlier, moved to another time or resource; or a block lifted. Read from what the save
// changes, so every way of doing it counts, now and later. OfferFreedSlotsBehavior publishes
// them once the request has succeeded.
public sealed class FreedSlotsInterceptor(FreedSlots freed) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Note(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries<Reservation>().Where(e => e.State == EntityState.Modified))
        {
            var original = entry.OriginalValues;
            if (original.GetValue<ReservationStatus>(nameof(Reservation.Status)) != ReservationStatus.Confirmed)
            {
                continue;
            }

            var resourceId = original.GetValue<Guid>(nameof(Reservation.ResourceId));
            var start = original.GetValue<DateTime>(nameof(Reservation.StartDateTime));
            var end = original.GetValue<DateTime>(nameof(Reservation.EndDateTime));
            var now = entry.Entity;
            if (now.Status != ReservationStatus.Confirmed || now.ResourceId != resourceId)
            {
                freed.Add(resourceId, start, end);
                continue;
            }

            // Still there, perhaps at another time: what it no longer covers.
            freed.Add(resourceId, start, Min(now.StartDateTime, end));
            freed.Add(resourceId, Max(now.EndDateTime, start), end);
        }

        foreach (var entry in context.ChangeTracker.Entries<ResourceBlock>().Where(e => e.State == EntityState.Deleted))
        {
            freed.Add(entry.Entity.ResourceId, entry.Entity.StartDateTime, entry.Entity.EndDateTime);
        }
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
