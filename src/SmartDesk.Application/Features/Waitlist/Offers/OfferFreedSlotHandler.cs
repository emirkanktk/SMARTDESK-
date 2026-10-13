using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Waitlist.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Waitlist.Offers;

// A time freed on a resource goes to whoever has waited longest for it (ADR-0043): what they
// waited for is booked for them, and their inbox says so. In the order people joined, so
// entries for times that don't overlap can all be booked from one freed slot.
public sealed class OfferFreedSlotHandler(
    IApplicationDbContext db,
    IUserAccounts accounts,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations inbox,
    TimeProvider timeProvider,
    ILogger<OfferFreedSlotHandler> logger) : INotificationHandler<SlotFreed>
{
    public async Task Handle(SlotFreed freed, CancellationToken cancellationToken)
    {
        // Whatever freed the time is done and committed: nothing here may fail it.
        try
        {
            await OfferAsync(freed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.DiscardChanges();
            logger.LogError(
                exception, "Offering {From}-{To} on resource {ResourceId} to its waiting list failed", freed.From, freed.To, freed.ResourceId);
        }
    }

    private async Task OfferAsync(SlotFreed freed, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var waiting = await db.WaitlistEntries
            .AsNoTracking()
            .Where(e => e.ResourceId == freed.ResourceId
                && e.StartDateTime < freed.To
                && freed.From < e.EndDateTime
                && e.EndDateTime > now)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        foreach (var entryId in waiting)
        {
            try
            {
                await TryBookAsync(freed.ResourceId, entryId, now, cancellationToken);
            }
            catch (ConflictException)
            {
                // Taken after all, by a booking the lock didn't see (ADR-0011): it waits on.
                db.DiscardChanges();
            }
        }
    }

    // Under the resource's lock, as any booking (ADR-0011). Books what the entry waits for if
    // it's free and the person may still use the resource; still taken, the entry stays in line.
    private async Task TryBookAsync(Guid resourceId, Guid entryId, DateTime now, CancellationToken cancellationToken)
    {
        Reservation reservation;
        IReadOnlyList<InboxMessage> messages;
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(resourceId, cancellationToken);

            // Left meanwhile, or booked by an offer just before this one.
            var entry = await db.WaitlistEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken);
            if (entry is null)
            {
                return;
            }

            // Once the time has begun, what's left of it, from the current minute.
            var begun = entry.StartDateTime < now;
            var start = begun ? new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc) : entry.StartDateTime;
            var resource = await db.Resources
                .AsNoTracking()
                .Where(r => r.Id == resourceId)
                .Select(r => new { r.Name, r.Status, r.CheckInRequired, Teams = r.Teams.Select(t => t.TeamId).ToList() })
                .SingleAsync(cancellationToken);
            var person = await accounts.FindAsync(entry.UserId, cancellationToken);
            var gone = (begun && entry.EndDateTime - start < WaitlistRules.MinimumLeft)
                || person is null
                || person.Status != AccountStatus.Active
                || !ResourceAccess.UsableBy(person, resource.Teams);
            if (gone)
            {
                // Too little of it left, or no longer theirs to book (ADR-0022): out of the line.
                db.WaitlistEntries.Remove(entry);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            // Out of use for now, or still taken: it waits on.
            if (resource.Status != ResourceStatus.Active)
            {
                return;
            }

            var conflicts = await BookingGuard.FindConflictsAsync(
                db, resourceId, resource.Name, [new Occurrence(start, entry.EndDateTime)], null, cancellationToken);
            if (conflicts[0] is not null)
            {
                return;
            }

            reservation = new Reservation
            {
                Id = Guid.CreateVersion7(),
                ResourceId = resourceId,
                UserId = entry.UserId,
                StartDateTime = start,
                EndDateTime = entry.EndDateTime,
                Status = ReservationStatus.Confirmed,
                ReminderMinutes = start - now > TimeSpan.FromMinutes(WaitlistRules.ReminderMinutes) ? WaitlistRules.ReminderMinutes : null,
                CheckInRequired = resource.CheckInRequired,
            };
            db.Reservations.Add(reservation);
            db.WaitlistEntries.Remove(entry);
            messages = inbox.Write(InboxMessageKind.WaitlistBooked, [entry.UserId], reservation, resource.Name, senderId: null);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Only after commit, as for any booking: calendars show it, and its owner is told.
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservation.Id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);
        await notifier.ReservationCreatedAsync(dto, cancellationToken);
        await inbox.SendAsync(messages, cancellationToken);
    }
}
