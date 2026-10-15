using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Calendar.Commands.CreateCalendarSubscription;

// A personal calendar link for the caller (ADR-0048), to subscribe to from a calendar app. One
// per person: a new link replaces the old, which stops working at once - the way to cut off a
// link that got out. The token is in the answer only; just its hash is kept.
public sealed record CreateCalendarSubscriptionCommand : IRequest<CalendarSubscriptionDto>;

public sealed class CreateCalendarSubscriptionCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, IFrontendLinks links, TimeProvider timeProvider)
    : IRequestHandler<CreateCalendarSubscriptionCommand, CalendarSubscriptionDto>
{
    public async Task<CalendarSubscriptionDto> Handle(CreateCalendarSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var feed = await db.CalendarFeeds.SingleOrDefaultAsync(f => f.UserId == userId, cancellationToken);
        if (feed is null)
        {
            feed = new CalendarFeed { UserId = userId };
            db.CalendarFeeds.Add(feed);
        }

        var token = CalendarFeedRules.NewToken();
        feed.TokenHash = CalendarFeedRules.HashToken(token);
        feed.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
        feed.LastFetchedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        return new CalendarSubscriptionDto(links.CalendarFeed(token), feed.CreatedAt, null);
    }
}
