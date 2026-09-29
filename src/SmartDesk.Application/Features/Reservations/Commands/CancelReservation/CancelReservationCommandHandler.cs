using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.CancelReservation;

public sealed class CancelReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<CancelReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        bool cancelledNow;

        // Locked so a concurrent update of the same reservation can't interleave with this.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(request.Id, cancellationToken);

            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);

            // Business rule 4: the owner, or an Admin managing reservations.
            if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException(Messages.Reservations_CancelOwnOnly);
            }

            // Idempotent: a retried or double-clicked cancel succeeds without changing anything
            // or broadcasting a second event.
            cancelledNow = reservation.Status != ReservationStatus.Cancelled;
            if (cancelledNow)
            {
                if (reservation.EndDateTime <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    throw new ConflictException(Messages.Reservations_EndedCantCancel);
                }

                reservation.Status = ReservationStatus.Cancelled;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        var dto = await ToDtoAsync(request.Id, cancellationToken);
        if (cancelledNow)
        {
            await notifier.ReservationCancelledAsync(dto, cancellationToken);
        }

        return dto;
    }

    private async Task<ReservationDto> ToDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);

        return await identityService.WithUserNameAsync(dto, cancellationToken);
    }
}
