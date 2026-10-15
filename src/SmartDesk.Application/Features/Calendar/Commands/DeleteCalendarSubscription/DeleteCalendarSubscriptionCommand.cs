using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Calendar.Commands.DeleteCalendarSubscription;

// No personal calendar link any more (ADR-0048): it stops working, and a calendar app that
// subscribed to it keeps what it last fetched. Nothing to do when there's none.
public sealed record DeleteCalendarSubscriptionCommand : IRequest;

public sealed class DeleteCalendarSubscriptionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DeleteCalendarSubscriptionCommand>
{
    public async Task Handle(DeleteCalendarSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var feed = await db.CalendarFeeds.SingleOrDefaultAsync(f => f.UserId == userId, cancellationToken);
        if (feed is not null)
        {
            db.CalendarFeeds.Remove(feed);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
