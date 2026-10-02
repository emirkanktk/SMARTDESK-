using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Queries.GetMyInvitations;

// The meetings the caller is invited to and that aren't over yet (ADR-0032), soonest first.
// Their own bookings are in GetMyReservationsQuery; these are other people's.
public sealed record GetMyInvitationsQuery : IRequest<IReadOnlyList<ReservationDto>>;

public sealed class GetMyInvitationsQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    TimeProvider timeProvider) : IRequestHandler<GetMyInvitationsQuery, IReadOnlyList<ReservationDto>>
{
    public async Task<IReadOnlyList<ReservationDto>> Handle(GetMyInvitationsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // On a resource they may still use: someone who has since left the team doesn't see
        // the team's room through an old invitation (ADR-0022).
        var invited = await db.Reservations
            .AsNoTracking()
            .OnUsableResources(currentUser)
            .Where(r => r.Status == ReservationStatus.Confirmed
                && r.EndDateTime > now
                && r.Attendees.Any(a => a.UserId == userId))
            .OrderBy(r => r.StartDateTime)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        return await identityService.WithUserNamesAsync(invited, cancellationToken);
    }
}
