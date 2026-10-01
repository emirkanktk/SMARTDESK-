using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.SendEndingSoonNotices;

public sealed class SendEndingSoonNoticesCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<SendEndingSoonNoticesCommand, int>
{
    public async Task<int> Handle(SendEndingSoonNoticesCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var horizon = now + ExtensionRules.NoticeBefore;
        var due = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.EndingSoonSentAt == null
                && r.StartDateTime <= now
                && r.EndDateTime > now
                && r.EndDateTime <= horizon)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var id in due)
        {
            if (await MarkSentAsync(id, now, horizon, cancellationToken))
            {
                var dto = await db.Reservations
                    .AsNoTracking()
                    .Where(r => r.Id == id)
                    .Select(ReservationDto.Projection)
                    .SingleAsync(cancellationToken);
                await notifier.ReservationEndingSoonAsync(
                    await identityService.WithUserNameAsync(dto, cancellationToken), cancellationToken);
                sent++;
            }
        }

        return sent;
    }

    // Under the reservation's own lock, like the reminders: one extended, cancelled or ended
    // early meanwhile is re-read here and skipped if it's no longer due. Marked before it's
    // sent, so a failed push is lost rather than repeated every few seconds.
    private async Task<bool> MarkSentAsync(Guid id, DateTime now, DateTime horizon, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockReservationAsync(id, cancellationToken);

        var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reservation is null || !IsDue(reservation, now, horizon))
        {
            return false;
        }

        reservation.EndingSoonSentAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsDue(Reservation r, DateTime now, DateTime horizon) =>
        r.Status == ReservationStatus.Confirmed
        && r.EndingSoonSentAt is null
        && r.StartDateTime <= now
        && r.EndDateTime > now
        && r.EndDateTime <= horizon;
}
