using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Queries.GetExtensionOptions;

// How much longer a booking can run (ADR-0033): the ExtensionRules.Options that end before the
// resource's next booking or blocked window, for the ending-soon notice's buttons. A snapshot:
// extending checks again.
public sealed record GetExtensionOptionsQuery(Guid Id) : IRequest<ExtensionOptionsDto>;

// The minutes the booking can still be extended by, shortest first; empty when it can't be.
public sealed record ExtensionOptionsDto(IReadOnlyList<int> Minutes);

public sealed class GetExtensionOptionsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IRequestHandler<GetExtensionOptionsQuery, ExtensionOptionsDto>
{
    public async Task<ExtensionOptionsDto> Handle(GetExtensionOptionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var reservation = await db.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), request.Id);

        // As for changing it any other way (business rule 4).
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

        // A resource the caller may no longer use, or that isn't Active, can't take more time.
        var bookable = await db.Resources
            .AsNoTracking()
            .UsableBy(currentUser)
            .AnyAsync(r => r.Id == reservation.ResourceId && r.Status == ResourceStatus.Active, cancellationToken);
        if (!bookable)
        {
            return new ExtensionOptionsDto([]);
        }

        var end = reservation.EndDateTime;
        var furthest = end.AddMinutes(ExtensionRules.MaxMinutes);
        var nextBooking = await db.Reservations
            .Where(r => r.ResourceId == reservation.ResourceId && r.Id != reservation.Id)
            .Where(ReservationOverlap.ConfirmedOverlapping(end, furthest))
            .MinAsync(r => (DateTime?)r.StartDateTime, cancellationToken);
        var nextBlock = await db.ResourceBlocks
            .Where(b => b.ResourceId == reservation.ResourceId)
            .Where(ResourceBlockOverlap.Overlapping(end, furthest))
            .MinAsync(b => (DateTime?)b.StartDateTime, cancellationToken);
        var limit = new[] { furthest, nextBooking ?? furthest, nextBlock ?? furthest }.Min();

        return new ExtensionOptionsDto(ExtensionRules.Options
            .Where(minutes => end.AddMinutes(minutes) <= limit
                && end.AddMinutes(minutes) - reservation.StartDateTime <= ReservationRules.MaxDuration)
            .ToList());
    }
}
