using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Meetings.Common;

namespace SmartDesk.Application.Features.Inbox.Queries.GetInbox;

// The caller's messages, newest first (ADR-0032): the latest InboxPageSize, read or not.
public sealed record GetInboxQuery : IRequest<IReadOnlyList<InboxMessageDto>>;

public sealed class GetInboxQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, IIdentityService identityService)
    : IRequestHandler<GetInboxQuery, IReadOnlyList<InboxMessageDto>>
{
    public async Task<IReadOnlyList<InboxMessageDto>> Handle(GetInboxQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var messages = await db.InboxMessages
            .AsNoTracking()
            .Where(m => m.RecipientId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(MeetingRules.InboxPageSize)
            .Select(InboxMessageDto.Projection)
            .ToListAsync(cancellationToken);

        var senders = messages.Select(m => m.SenderId).OfType<Guid>().Distinct().ToList();
        if (senders.Count == 0)
        {
            return messages;
        }

        var names = await identityService.GetDisplayNamesAsync(senders, cancellationToken);
        return messages
            .Select(m => m with { SenderName = m.SenderId is { } sender ? names.GetValueOrDefault(sender) : null })
            .ToList();
    }
}
