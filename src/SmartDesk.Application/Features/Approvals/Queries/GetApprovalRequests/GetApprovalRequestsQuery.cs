using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Approvals.Queries.GetApprovalRequests;

// What waits for an Admin's approval (ADR-0049), the soonest first: it lapses at its start.
// For Admins, on every resource.
public sealed record GetApprovalRequestsQuery : IRequest<IReadOnlyList<ReservationDto>>;

public sealed class GetApprovalRequestsQueryHandler(IApplicationDbContext db, IIdentityService identityService)
    : IRequestHandler<GetApprovalRequestsQuery, IReadOnlyList<ReservationDto>>
{
    public async Task<IReadOnlyList<ReservationDto>> Handle(GetApprovalRequestsQuery request, CancellationToken cancellationToken)
    {
        var waiting = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending)
            .OrderBy(r => r.StartDateTime)
            .ThenBy(r => r.RequestedAt)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        return await identityService.WithUserNamesAsync(waiting, cancellationToken);
    }
}
