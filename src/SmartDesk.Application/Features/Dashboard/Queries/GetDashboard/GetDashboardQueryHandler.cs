using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    TimeProvider timeProvider) : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    public const int MyUpcomingLimit = 5;

    public async Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (todayStart, todayEnd) = zone.LocalDayBoundsUtc(now);

        var reservations = db.Reservations.AsNoTracking();

        var today = await reservations
            .Where(ReservationOverlap.ConfirmedOverlapping(todayStart, todayEnd))
            .OrderBy(r => r.StartDateTime)
            .ThenBy(r => r.Resource.Name)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        var inUseNow = await reservations
            .Where(ReservationOverlap.ConfirmedActiveAt(now))
            .OrderBy(r => r.EndDateTime)
            .ThenBy(r => r.Resource.Name)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        var myUpcomingQuery = reservations
            .Where(r => r.UserId == userId && r.Status == ReservationStatus.Confirmed && r.StartDateTime > now);
        var myUpcomingCount = await myUpcomingQuery.CountAsync(cancellationToken);
        var myUpcoming = await myUpcomingQuery
            .OrderBy(r => r.StartDateTime)
            .Take(MyUpcomingLimit)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);

        var activeResources = await db.Resources.CountAsync(r => r.Status == ResourceStatus.Active, cancellationToken);

        // One name lookup for all three lists.
        var names = await identityService.GetDisplayNamesAsync(
            today.Concat(inUseNow).Concat(myUpcoming).Select(r => r.UserId).Distinct().ToList(),
            cancellationToken);
        IReadOnlyList<ReservationDto> Named(List<ReservationDto> list) =>
            list.Select(r => r with { UserDisplayName = names.GetValueOrDefault(r.UserId) }).ToList();

        return new DashboardDto(
            now,
            zone.Id,
            todayStart,
            todayEnd,
            new DashboardStats(activeResources, inUseNow.Count, today.Count, myUpcomingCount),
            Named(today),
            Named(inUseNow),
            Named(myUpcoming));
    }
}
