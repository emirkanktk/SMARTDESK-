using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Approvals.Commands.ApproveReservation;

// An Admin approves a booking that waits for one (ADR-0049): it's booked from now on - reminded,
// checked in, counted - and whoever asked is told. 409 when it isn't waiting, and when its start
// has come: a request still waiting then lapses.
public sealed record ApproveReservationCommand(Guid Id) : IRequest<ReservationDto>;

public sealed class ApproveReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations inbox,
    BookingApprovals approvals,
    TimeProvider timeProvider) : IRequestHandler<ApproveReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(ApproveReservationCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedException();
        IReadOnlyList<InboxMessage> messages;
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            var reservation = await approvals.FindWaitingAsync(request.Id, cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (reservation.StartDateTime <= now)
            {
                throw new ConflictException(Messages.Approval_Lapsed) { Code = ConflictCodes.ApprovalLapsed };
            }

            reservation.Status = ReservationStatus.Confirmed;
            reservation.DecidedAt = now;
            reservation.DecidedByUserId = adminId;
            messages = inbox.Write(InboxMessageKind.BookingApproved, [reservation.UserId], reservation, reservation.Resource.Name, adminId);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await approvals.ReadAsync(request.Id, identityService, cancellationToken);
        await notifier.ReservationUpdatedAsync(dto, cancellationToken);
        await inbox.SendAsync(messages, cancellationToken);
        return dto;
    }
}
