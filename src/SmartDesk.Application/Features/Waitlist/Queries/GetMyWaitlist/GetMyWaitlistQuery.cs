using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Waitlist.Common;

namespace SmartDesk.Application.Features.Waitlist.Queries.GetMyWaitlist;

// What the caller is waiting for (ADR-0043), soonest first. Times gone by are left out.
public sealed record GetMyWaitlistQuery : IRequest<IReadOnlyList<WaitlistEntryDto>>;

public sealed class GetMyWaitlistQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<GetMyWaitlistQuery, IReadOnlyList<WaitlistEntryDto>>
{
    public async Task<IReadOnlyList<WaitlistEntryDto>> Handle(GetMyWaitlistQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mine = db.WaitlistEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.EndDateTime > now)
            .OrderBy(e => e.StartDateTime)
            .ThenBy(e => e.CreatedAt);

        return await WaitlistReader.Read(db, mine, now).ToListAsync(cancellationToken);
    }
}
