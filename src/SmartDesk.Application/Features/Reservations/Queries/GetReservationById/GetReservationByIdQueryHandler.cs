using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Constants;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservationById;

public sealed class GetReservationByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService) : IRequestHandler<GetReservationByIdQuery, ReservationDto>
{
    public async Task<ReservationDto> Handle(GetReservationByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var query = db.Reservations.AsNoTracking().Where(r => r.Id == request.Id);

        // Your own reservation always; anyone else's only on a resource you may use (ADR-0022).
        if (!currentUser.IsInRole(Roles.Admin))
        {
            var teamId = currentUser.TeamId;
            query = query.Where(r =>
                r.UserId == userId || !r.Resource.Teams.Any() || r.Resource.Teams.Any(t => t.TeamId == teamId));
        }

        var dto = await query
            .Select(ReservationDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), request.Id);

        return await identityService.WithUserNameAsync(dto, cancellationToken);
    }
}
