using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.ReleaseNoShows;

public sealed class ReleaseNoShowsCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    MeetingInvitations invitations,
    ICheckInTimes times) : IRequestHandler<ReleaseNoShowsCommand, int>
{
    public async Task<int> Handle(ReleaseNoShowsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await db.Reservations
            .AsNoTracking()
            .Where(CheckInRules.DueForRelease(now, times.Grace))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var released = 0;
        foreach (var id in due)
        {
            var messages = await ReleaseAsync(id, now, cancellationToken);
            if (messages is null)
            {
                continue;
            }

            var dto = await db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(ReservationDto.Projection)
                .SingleAsync(cancellationToken);
            dto = await identityService.WithUserNameAsync(dto, cancellationToken);

            // Everyone's screens free the slot; the owner is also told why it's gone, and the
            // people invited get a message (ADR-0032).
            await notifier.ReservationCancelledAsync(dto, cancellationToken);
            await notifier.ReservationReleasedAsync(dto, cancellationToken);
            await invitations.SendAsync(messages, cancellationToken);
            released++;
        }

        return released;
    }

    // Under the reservation's own lock, like the check-in: one checked in, moved or cancelled
    // meanwhile no longer passes the re-read, and stays as it is. Null when it isn't released;
    // otherwise the messages for its attendees, from nobody.
    private async Task<IReadOnlyList<InboxMessage>?> ReleaseAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockReservationAsync(id, cancellationToken);

        var reservation = await db.Reservations
            .Where(r => r.Id == id)
            .Where(CheckInRules.DueForRelease(now, times.Grace))
            .SingleOrDefaultAsync(cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        reservation.Status = ReservationStatus.Cancelled;
        reservation.ReleasedAt = now;
        var messages = await invitations.WriteCancellationAsync(reservation, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }
}
