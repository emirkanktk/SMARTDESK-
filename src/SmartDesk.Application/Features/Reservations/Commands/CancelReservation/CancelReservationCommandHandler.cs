using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.CancelReservation;

public sealed class CancelReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<CancelReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), request.Id);

        // Business rule 4: the owner, or an Admin managing reservations.
        if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenAccessException("You can only cancel your own reservations.");
        }

        // Idempotent: a retried or double-clicked cancel succeeds without changing anything
        // or broadcasting a second event.
        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return await ToDtoAsync(reservation.Id, cancellationToken);
        }

        if (reservation.EndDateTime <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ConflictException("This reservation has already ended and can't be cancelled.");
        }

        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);

        var dto = await ToDtoAsync(reservation.Id, cancellationToken);
        await notifier.ReservationCancelledAsync(dto, cancellationToken);

        return dto;
    }

    private Task<ReservationDto> ToDtoAsync(Guid id, CancellationToken cancellationToken) =>
        db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
}
