using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.EndReservation;

// "We're done": a booking under way ends now, so the room is free for others at once
// (ADR-0034). For the owner or an Admin, like any change. One that hasn't started is cancelled,
// not ended.
public sealed record EndReservationCommand(Guid Id) : IRequest<ReservationDto>;

public sealed class EndReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<EndReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(EndReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // Under the reservation's lock, like every other change to it (ADR-0013). Ending early
        // claims nothing new, so the resource's lock isn't needed.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(request.Id, cancellationToken);

            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);

            if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException(Messages.Reservations_ChangeOwnOnly);
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException(Messages.Reservations_CancelledCantChange);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (reservation.EndDateTime <= now)
            {
                throw new ConflictException(Messages.Reservations_EndedCantChange);
            }

            if (reservation.StartDateTime >= now)
            {
                throw new ConflictException(Messages.Reservations_EndNotStarted);
            }

            reservation.EndDateTime = now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        // Calendars free the rest of the slot.
        await notifier.ReservationUpdatedAsync(dto, cancellationToken);
        return dto;
    }
}
