using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Inbox.Queries.GetUnreadCount;

// How many of the caller's messages are unread (ADR-0032), for the count in the menu.
public sealed record GetUnreadInboxCountQuery : IRequest<UnreadInboxCountDto>;

public sealed record UnreadInboxCountDto(int Unread);

public sealed class GetUnreadInboxCountQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetUnreadInboxCountQuery, UnreadInboxCountDto>
{
    public async Task<UnreadInboxCountDto> Handle(GetUnreadInboxCountQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var unread = await db.InboxMessages.CountAsync(m => m.RecipientId == userId && m.ReadAt == null, cancellationToken);
        return new UnreadInboxCountDto(unread);
    }
}
