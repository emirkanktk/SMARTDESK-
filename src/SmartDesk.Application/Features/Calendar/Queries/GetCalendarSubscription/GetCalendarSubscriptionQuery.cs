using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Calendar.Common;

namespace SmartDesk.Application.Features.Calendar.Queries.GetCalendarSubscription;

// Whether the caller has a personal calendar link (ADR-0048), since when, and when a calendar
// app last fetched it; null when they have none. Never the link itself.
public sealed record GetCalendarSubscriptionQuery : IRequest<CalendarSubscriptionDto?>;

public sealed class GetCalendarSubscriptionQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetCalendarSubscriptionQuery, CalendarSubscriptionDto?>
{
    public async Task<CalendarSubscriptionDto?> Handle(GetCalendarSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        return await db.CalendarFeeds
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => new CalendarSubscriptionDto(null, f.CreatedAt, f.LastFetchedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
