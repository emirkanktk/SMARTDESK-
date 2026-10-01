using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.CancelFollowing;

// Cancels a date of a repeating booking and every later one still to come (ADR-0035), for its
// owner or an Admin: how a series ends early. Dates already over or cancelled stay as they are.
// Everyone invited to any of them gets one message for the lot. Returns the dates cancelled now;
// none when there was nothing left to cancel.
public sealed record CancelFollowingCommand(Guid Id) : IRequest<IReadOnlyList<ReservationDto>>;

public sealed class CancelFollowingCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    MeetingInvitations invitations) : IRequestHandler<CancelFollowingCommand, IReadOnlyList<ReservationDto>>
{
    public async Task<IReadOnlyList<ReservationDto>> Handle(CancelFollowingCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        List<Reservation> cancelled;
        IReadOnlyList<InboxMessage> messages = [];

        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            var reservation = await db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == request.Id)
                .Select(r => new { r.UserId, r.SeriesId, r.StartDateTime, r.EndDateTime })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);

            // Business rule 4, as for cancelling one date.
            if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException(Messages.Reservations_CancelOwnOnly);
            }

            if (reservation.SeriesId is not { } seriesId)
            {
                throw new ConflictException(Messages.Series_NotASeries);
            }

            if (reservation.EndDateTime <= now)
            {
                throw new ConflictException(Messages.Reservations_EndedCantCancel);
            }

            // Locked in one order - by id - like every other command that locks several of a
            // series' dates, so two of them can't each wait for the other.
            var ids = await db.Reservations
                .Where(r => r.SeriesId == seriesId && r.StartDateTime >= reservation.StartDateTime)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in ids)
            {
                await db.LockReservationAsync(id, cancellationToken);
            }

            // Read again under the locks: a date may have been cancelled, or have ended, meanwhile.
            cancelled = await db.Reservations
                .Include(r => r.Attendees)
                .Where(r => ids.Contains(r.Id) && r.Status == ReservationStatus.Confirmed && r.EndDateTime > now)
                .OrderBy(r => r.StartDateTime)
                .ToListAsync(cancellationToken);
            if (cancelled.Count > 0)
            {
                foreach (var date in cancelled)
                {
                    date.Status = ReservationStatus.Cancelled;
                }

                var attendees = cancelled.SelectMany(r => r.Attendees.Select(a => a.UserId)).Distinct().ToList();
                if (attendees.Count > 0)
                {
                    var series = await db.ReservationSeries.SingleAsync(s => s.Id == seriesId, cancellationToken);
                    var resourceName = await db.Resources
                        .Where(r => r.Id == cancelled[0].ResourceId)
                        .Select(r => r.Name)
                        .SingleAsync(cancellationToken);
                    messages = invitations.Write(
                        InboxMessageKind.SeriesCancelled, attendees, cancelled[0], resourceName, userId, series: series);
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        if (cancelled.Count == 0)
        {
            return [];
        }

        var cancelledIds = cancelled.Select(r => r.Id).ToList();
        var dtos = await db.Reservations
            .AsNoTracking()
            .Where(r => cancelledIds.Contains(r.Id))
            .OrderBy(r => r.StartDateTime)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);
        var named = await identityService.WithUserNamesAsync(dtos, cancellationToken);

        foreach (var dto in named)
        {
            await notifier.ReservationCancelledAsync(dto, cancellationToken);
        }

        await invitations.SendAsync(messages, cancellationToken);
        return named;
    }
}
