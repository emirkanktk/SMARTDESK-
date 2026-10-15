using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;

public sealed class RespondToInvitationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    MeetingInvitations invitations) : IRequestHandler<RespondToInvitationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(RespondToInvitationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var reason = request.Response == AttendeeResponse.Declined ? request.Reason?.Trim() : null;
        IReadOnlyList<InboxMessage> messages = [];
        IReadOnlyList<Guid> answered = [];

        // Under the reservations' locks, like update and cancel: an answer never lands on a
        // meeting that's just been moved or cancelled, or that has just lost them from its list.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            // In one order - by id - as every command that locks several dates of a series does.
            var dates = await DatesAsync(request, userId, cancellationToken);
            foreach (var id in dates.Order())
            {
                await db.LockReservationAsync(id, cancellationToken);
            }

            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);
            var attendee = await db.ReservationAttendees
                .SingleOrDefaultAsync(a => a.ReservationId == reservation.Id && a.UserId == userId, cancellationToken)
                ?? throw new ForbiddenAccessException(Messages.Meetings_NotInvited);

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException(Messages.Meetings_AnswerCancelled);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (reservation.EndDateTime <= now)
            {
                throw new ConflictException(Messages.Meetings_AnswerEnded);
            }

            // With Following, the later dates of its series too (ADR-0035): those still on, not
            // over, and that they're still invited to.
            var forSeries = request.Following && reservation.SeriesId is not null;
            var later = forSeries
                ? await db.ReservationAttendees
                    .Where(a => a.UserId == userId
                        && a.ReservationId != reservation.Id
                        && dates.Contains(a.ReservationId)
                        && a.Reservation.Status != ReservationStatus.Cancelled
                        && a.Reservation.EndDateTime > now)
                    .ToListAsync(cancellationToken)
                : [];

            // The same answer again (a double click, or a retry) changes nothing and tells nobody.
            var changing = later
                .Prepend(attendee)
                .Where(a => a.Response != request.Response || a.DeclineReason != reason)
                .ToList();
            if (changing.Count > 0)
            {
                foreach (var answer in changing)
                {
                    answer.Response = request.Response;
                    answer.DeclineReason = reason;
                    answer.RespondedAt = now;
                }

                var resourceName = await db.Resources
                    .Where(r => r.Id == reservation.ResourceId)
                    .Select(r => r.Name)
                    .SingleAsync(cancellationToken);
                var accepted = request.Response == AttendeeResponse.Accepted;
                if (forSeries)
                {
                    // One message for the dates answered, from this one on.
                    var series = await db.ReservationSeries.SingleAsync(s => s.Id == reservation.SeriesId, cancellationToken);
                    var kind = accepted ? InboxMessageKind.SeriesAccepted : InboxMessageKind.SeriesDeclined;
                    messages = invitations.Write(kind, [reservation.UserId], reservation, resourceName, userId, reason, series);
                }
                else
                {
                    var kind = accepted ? InboxMessageKind.MeetingAccepted : InboxMessageKind.MeetingDeclined;
                    messages = invitations.Write(kind, [reservation.UserId], reservation, resourceName, userId, reason);
                }

                answered = changing.Select(a => a.ReservationId).ToList();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        var dtos = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id || answered.Contains(r.Id))
            .OrderBy(r => r.StartDateTime)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);
        var named = await identityService.WithUserNamesAsync(dtos, cancellationToken);

        if (messages.Count > 0)
        {
            // Every screen showing one of the meetings gets the new answer; the organizer also
            // gets the message, with the reason.
            foreach (var dto in named)
            {
                await notifier.ReservationUpdatedAsync(dto, cancellationToken);
            }

            await invitations.SendAsync(messages, cancellationToken);
        }

        return named.Single(r => r.Id == request.Id);
    }

    // The reservations the answer may touch, to lock: this one, and with Following the later
    // dates of its series the caller is invited to. Read before the locks; what's read under
    // them decides.
    private async Task<IReadOnlyList<Guid>> DatesAsync(RespondToInvitationCommand request, Guid userId, CancellationToken cancellationToken)
    {
        if (!request.Following)
        {
            return [request.Id];
        }

        var reservation = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(r => new { r.SeriesId, r.StartDateTime })
            .SingleOrDefaultAsync(cancellationToken);
        if (reservation?.SeriesId is not { } seriesId)
        {
            return [request.Id];
        }

        var later = await db.Reservations
            .Where(r => r.SeriesId == seriesId
                && r.StartDateTime >= reservation.StartDateTime
                && r.Attendees.Any(a => a.UserId == userId))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        return later.Append(request.Id).Distinct().ToList();
    }
}
