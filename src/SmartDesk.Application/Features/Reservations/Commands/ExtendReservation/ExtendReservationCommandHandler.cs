using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;

public sealed class ExtendReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    MeetingInvitations invitations,
    BookingPolicy policy) : IRequestHandler<ExtendReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(ExtendReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        IReadOnlyList<InboxMessage> messages;

        // The same locks as moving a booking (ADR-0011, ADR-0013): the reservation's own, then
        // the resource's, so nobody books the extra time while this checks it.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(request.Id, cancellationToken);

            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);

            // Business rule 4: the owner, or an Admin managing reservations.
            if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException(Messages.Reservations_ChangeOwnOnly);
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException(Messages.Reservations_CancelledCantChange);
            }

            if (reservation.EndDateTime <= timeProvider.GetUtcNow().UtcDateTime)
            {
                throw new ConflictException(Messages.Reservations_EndedCantChange);
            }

            // As long as its kind allows, and no later than closing time when it keeps to office
            // hours (ADR-0046).
            var end = reservation.EndDateTime.AddMinutes(request.Minutes);
            await policy.EnsureCanRunOnAsync(reservation.ResourceId, reservation.StartDateTime, end, cancellationToken);

            await db.LockResourceForBookingAsync(reservation.ResourceId, cancellationToken);

            // Only the extra time is a new claim on the resource; its booking checks are the
            // same as for any other, with a clearer word for the most likely clash.
            BookableResource resource;
            try
            {
                resource = await BookingGuard.EnsureBookableAsync(
                    db, currentUser, reservation.ResourceId, reservation.EndDateTime, end, reservation.Id, cancellationToken);
            }
            catch (ConflictException e) when (e.Code == ConflictCodes.ReservationOverlaps)
            {
                var name = await db.Resources
                    .Where(r => r.Id == reservation.ResourceId)
                    .Select(r => r.Name)
                    .SingleAsync(cancellationToken);
                throw new ConflictException(string.Format(Messages.Reservations_ExtensionTaken, name, request.Minutes))
                {
                    Code = ConflictCodes.ReservationOverlaps,
                };
            }

            reservation.EndDateTime = end;

            // Asked again before the new end, if it runs on once more.
            reservation.EndingSoonSentAt = null;

            var attendees = await db.ReservationAttendees
                .Where(a => a.ReservationId == reservation.Id)
                .Select(a => a.UserId)
                .ToListAsync(cancellationToken);
            messages = invitations.Write(InboxMessageKind.MeetingExtended, attendees, reservation, resource.Name, userId);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        await notifier.ReservationUpdatedAsync(dto, cancellationToken);
        await invitations.SendAsync(messages, cancellationToken);
        return dto;
    }
}
