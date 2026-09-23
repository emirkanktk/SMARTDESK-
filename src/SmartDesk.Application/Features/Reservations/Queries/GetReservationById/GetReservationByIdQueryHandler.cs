using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservationById;

public sealed class GetReservationByIdQueryHandler(IApplicationDbContext db, IIdentityService identityService)
    : IRequestHandler<GetReservationByIdQuery, ReservationDto>
{
    public async Task<ReservationDto> Handle(GetReservationByIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ReservationDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), request.Id);

        return await identityService.WithUserNameAsync(dto, cancellationToken);
    }
}
