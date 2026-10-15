using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservations;

public sealed class GetReservationsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService)
    : IRequestHandler<GetReservationsQuery, IReadOnlyList<ReservationDto>>
{
    public async Task<IReadOnlyList<ReservationDto>> Handle(GetReservationsQuery request, CancellationToken cancellationToken)
    {
        var from = request.From.AsUtc();
        var to = request.To.AsUtc();

        // Same overlap rule as availability and booking: a reservation shows up in a range iff
        // it would block a booking of that range.
        // Only on resources the caller may use: another team's schedule is theirs (ADR-0022).
        var query = db.Reservations
            .AsNoTracking()
            .OnUsableResources(currentUser)
            .Where(ReservationOverlap.TakenOverlapping(from, to));

        if (request.ResourceId is { } resourceId)
        {
            query = query.Where(r => r.ResourceId == resourceId);
        }

        if (request.ResourceTypeId is { } typeId)
        {
            query = query.Where(r => r.Resource.ResourceTypeId == typeId);
        }

        var reservations = await query
            .OrderBy(r => r.StartDateTime)
            .ThenBy(r => r.Resource.Name)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        return await identityService.WithUserNamesAsync(reservations, cancellationToken);
    }
}
