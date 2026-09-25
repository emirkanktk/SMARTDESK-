using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.SendDueReminders;

public sealed class SendDueRemindersCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<SendDueRemindersCommand, int>
{
    public async Task<int> Handle(SendDueRemindersCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // No reminder is set further ahead than the longest option, so only reservations that
        // start within it can be due. Which of those are due is decided in memory: "start minus
        // the row's own minutes" isn't something every provider translates the same way.
        var horizon = now.AddMinutes(ReservationRules.MaxReminderMinutes);
        var candidates = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.ReminderMinutes != null
                && r.ReminderSentAt == null
                && r.StartDateTime > now
                && r.StartDateTime <= horizon)
            .Select(r => new { r.Id, r.StartDateTime, r.ReminderMinutes })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var candidate in candidates.Where(c => c.StartDateTime.AddMinutes(-c.ReminderMinutes!.Value) <= now))
        {
            if (await MarkSentAsync(candidate.Id, now, cancellationToken))
            {
                var dto = await db.Reservations
                    .AsNoTracking()
                    .Where(r => r.Id == candidate.Id)
                    .Select(ReservationDto.Projection)
                    .SingleAsync(cancellationToken);
                await notifier.ReservationStartingSoonAsync(
                    await identityService.WithUserNameAsync(dto, cancellationToken), cancellationToken);
                sent++;
            }
        }

        return sent;
    }

    // Under the reservation's own lock, like update and cancel (ADR-0013): one moved, cancelled
    // or re-timed meanwhile is re-read here, and skipped if it's no longer due. Marking before
    // sending means a failed push is lost rather than repeated every few seconds.
    private async Task<bool> MarkSentAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockReservationAsync(id, cancellationToken);

        var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reservation is null || !IsDue(reservation, now))
        {
            return false;
        }

        reservation.ReminderSentAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsDue(Reservation r, DateTime now) =>
        r.Status == ReservationStatus.Confirmed
        && r.ReminderMinutes is { } minutes
        && r.ReminderSentAt is null
        && r.StartDateTime > now
        && r.StartDateTime.AddMinutes(-minutes) <= now;
}
