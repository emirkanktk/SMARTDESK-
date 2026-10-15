using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Approvals.Commands.LapseUnapprovedRequests;

// Lets go of the requests nobody approved before their start (ADR-0049), run every few seconds
// by ApprovalLapseWorker: each is cancelled, its time free again, and whoever asked is told, as
// is anyone invited. Returns how many lapsed.
public sealed record LapseUnapprovedRequestsCommand : IRequest<int>;

public sealed class LapseUnapprovedRequestsCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations inbox,
    BookingApprovals approvals,
    TimeProvider timeProvider) : IRequestHandler<LapseUnapprovedRequestsCommand, int>
{
    public async Task<int> Handle(LapseUnapprovedRequestsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending && r.StartDateTime <= now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var lapsed = 0;
        foreach (var id in due)
        {
            var messages = await LapseAsync(id, now, cancellationToken);
            if (messages is null)
            {
                continue;
            }

            await notifier.ReservationCancelledAsync(await approvals.ReadAsync(id, identityService, cancellationToken), cancellationToken);
            await inbox.SendAsync(messages, cancellationToken);
            lapsed++;
        }

        return lapsed;
    }

    // Under the booking's own lock: one approved, declined or cancelled meanwhile no longer
    // passes the re-read, and stays as it is. Null when it doesn't lapse.
    private async Task<IReadOnlyList<InboxMessage>?> LapseAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockReservationAsync(id, cancellationToken);

        var reservation = await db.Reservations
            .Include(r => r.Resource)
            .Where(r => r.Id == id && r.Status == ReservationStatus.Pending && r.StartDateTime <= now)
            .SingleOrDefaultAsync(cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        reservation.Status = ReservationStatus.Cancelled;
        reservation.DecidedAt = now;
        List<InboxMessage> messages =
        [
            .. inbox.Write(InboxMessageKind.ApprovalLapsed, [reservation.UserId], reservation, reservation.Resource.Name, senderId: null),
            .. await inbox.WriteCancellationAsync(reservation, null, cancellationToken),
        ];

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }
}
