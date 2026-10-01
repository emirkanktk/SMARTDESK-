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

        // Under the reservation's lock, like update and cancel: an answer never lands on a
        // meeting that's just been moved or cancelled, or that has just lost them from its list.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(request.Id, cancellationToken);

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

            // The same answer again (a double click, or a retry) changes nothing and tells nobody.
            if (attendee.Response != request.Response || attendee.DeclineReason != reason)
            {
                attendee.Response = request.Response;
                attendee.DeclineReason = reason;
                attendee.RespondedAt = now;

                var resourceName = await db.Resources
                    .Where(r => r.Id == reservation.ResourceId)
                    .Select(r => r.Name)
                    .SingleAsync(cancellationToken);
                var kind = request.Response == AttendeeResponse.Accepted
                    ? InboxMessageKind.MeetingAccepted
                    : InboxMessageKind.MeetingDeclined;
                messages = invitations.Write(kind, [reservation.UserId], reservation, resourceName, userId, reason);

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        if (messages.Count > 0)
        {
            // Every screen showing the meeting gets the new answer; the organizer also gets
            // the message, with the reason.
            await notifier.ReservationUpdatedAsync(dto, cancellationToken);
            await invitations.SendAsync(messages, cancellationToken);
        }

        return dto;
    }
}
