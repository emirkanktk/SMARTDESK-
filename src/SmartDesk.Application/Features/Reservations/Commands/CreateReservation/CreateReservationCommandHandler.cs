using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

public sealed class CreateReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier) : IRequestHandler<CreateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(CreateReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var reservationId = Guid.CreateVersion7();

        // Concurrency (business rule 5, ADR-0011): bookings of the same resource are serialized
        // by a lock held until commit, so the checks below always see the latest committed
        // reservations. The database's exclusion constraint stays as the last line of defence;
        // if it ever fires, Infrastructure reports it as the same ConflictException.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            var resource = await db.Resources
                .AsNoTracking()
                .Where(r => r.Id == request.ResourceId)
                .Select(r => new { r.Name, r.Status })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.ResourceId);

            // Business rule 3.
            if (resource.Status != ResourceStatus.Active)
            {
                throw new ConflictException($"{resource.Name} can't be reserved right now (status: {resource.Status}).");
            }

            // Business rule 1.
            var overlaps = await db.Reservations
                .Where(r => r.ResourceId == request.ResourceId)
                .AnyAsync(ReservationOverlap.ConfirmedOverlapping(start, end), cancellationToken);
            if (overlaps)
            {
                throw new ConflictException($"{resource.Name} is already reserved for part of that time.");
            }

            db.Reservations.Add(new Reservation
            {
                Id = reservationId,
                ResourceId = request.ResourceId,
                UserId = userId,
                StartDateTime = start,
                EndDateTime = end,
                Status = ReservationStatus.Confirmed,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Only after commit: never announce a reservation that could still roll back.
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        await notifier.ReservationCreatedAsync(dto, cancellationToken);

        return dto;
    }
}
