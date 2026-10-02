using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Inbox.Commands.MarkAllRead;

// Marks every unread message of the caller's read (ADR-0032), in one statement.
public sealed record MarkAllInboxMessagesReadCommand : IRequest;

public sealed class MarkAllInboxMessagesReadCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<MarkAllInboxMessagesReadCommand>
{
    public async Task Handle(MarkAllInboxMessagesReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await db.InboxMessages
            .Where(m => m.RecipientId == userId && m.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ReadAt, now), cancellationToken);
    }
}
