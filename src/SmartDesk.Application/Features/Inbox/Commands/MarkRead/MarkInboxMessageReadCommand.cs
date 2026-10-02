using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Inbox.Commands.MarkRead;

// Marks one of the caller's messages read (ADR-0032). Someone else's is not found, like one
// that doesn't exist; marking one that's read already changes nothing.
public sealed record MarkInboxMessageReadCommand(Guid Id) : IRequest;

public sealed class MarkInboxMessageReadCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<MarkInboxMessageReadCommand>
{
    public async Task Handle(MarkInboxMessageReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var message = await db.InboxMessages
            .SingleOrDefaultAsync(m => m.Id == request.Id && m.RecipientId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(InboxMessage), request.Id);

        if (message.ReadAt is null)
        {
            message.ReadAt = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
