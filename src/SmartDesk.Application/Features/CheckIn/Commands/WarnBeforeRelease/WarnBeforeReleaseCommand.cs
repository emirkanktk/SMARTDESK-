using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.WarnBeforeRelease;

// Five minutes before a booking nobody has checked in to is released, its owner and those
// invited hear about it, once (ADR-0034), while there's still time to get to the door - or at
// its start, with a shorter grace (ADR-0040). Run every few seconds by ReleaseWarningWorker;
// returns how many were warned.
public sealed record WarnBeforeReleaseCommand : IRequest<int>;

public sealed class WarnBeforeReleaseCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    ICheckInTimes times) : IRequestHandler<WarnBeforeReleaseCommand, int>
{
    public async Task<int> Handle(WarnBeforeReleaseCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await db.Reservations
            .AsNoTracking()
            .Where(CheckInRules.DueForReleaseWarning(now, times.Grace))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var warned = 0;
        foreach (var id in due)
        {
            if (!await MarkWarnedAsync(id, now, cancellationToken))
            {
                continue;
            }

            var dto = await db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(ReservationDto.Projection)
                .SingleAsync(cancellationToken);
            dto = await identityService.WithUserNameAsync(dto, cancellationToken);
            await notifier.ReservationReleaseWarningAsync(
                dto, [dto.UserId, .. dto.Attendees.Select(a => a.Id)], cancellationToken);
            warned++;
        }

        return warned;
    }

    // Under the reservation's lock, like the release: one checked in, moved or cancelled
    // meanwhile is re-read and skipped. Marked before it's sent, so a failed push isn't repeated.
    private async Task<bool> MarkWarnedAsync(Guid id, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockReservationAsync(id, cancellationToken);

        var reservation = await db.Reservations
            .Where(r => r.Id == id)
            .Where(CheckInRules.DueForReleaseWarning(now, times.Grace))
            .SingleOrDefaultAsync(cancellationToken);
        if (reservation is null)
        {
            return false;
        }

        reservation.ReleaseWarningSentAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
