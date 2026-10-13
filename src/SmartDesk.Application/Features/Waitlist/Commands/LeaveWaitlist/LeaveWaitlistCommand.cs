using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Waitlist.Commands.LeaveWaitlist;

// No longer waiting (ADR-0043). Only the person waiting; anyone else's entry isn't there (404).
public sealed record LeaveWaitlistCommand(Guid Id) : IRequest;

public sealed class LeaveWaitlistCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<LeaveWaitlistCommand>
{
    public async Task Handle(LeaveWaitlistCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var entry = await db.WaitlistEntries
            .SingleOrDefaultAsync(e => e.Id == request.Id && e.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(WaitlistEntry), request.Id);

        db.WaitlistEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
