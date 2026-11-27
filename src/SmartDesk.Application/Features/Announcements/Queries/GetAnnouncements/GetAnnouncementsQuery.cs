using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Announcements.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Announcements.Queries.GetAnnouncements;

// What's showing now (GET /api/announcements, everyone, ADR-0065): the announcements that haven't
// ended, newest first, at most AnnouncementRules.MaxShown.
public sealed record GetCurrentAnnouncementsQuery : IRequest<IReadOnlyList<AnnouncementDto>>;

// Every announcement, ended ones too, newest first (GET /api/announcements/all, Admin).
public sealed record GetAllAnnouncementsQuery : IRequest<IReadOnlyList<AnnouncementDto>>;

public sealed class GetAnnouncementsQueryHandler(IApplicationDbContext db, IIdentityService identityService, TimeProvider timeProvider)
    : IRequestHandler<GetCurrentAnnouncementsQuery, IReadOnlyList<AnnouncementDto>>,
      IRequestHandler<GetAllAnnouncementsQuery, IReadOnlyList<AnnouncementDto>>
{
    // The Admins' list: a season's worth is plenty to look back on.
    public const int MaxListed = 100;

    public async Task<IReadOnlyList<AnnouncementDto>> Handle(GetCurrentAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await db.Announcements
            .AsNoTracking()
            .Where(a => a.EndsAt > now)
            .OrderByDescending(a => a.CreatedAt)
            .Take(AnnouncementRules.MaxShown)
            .Select(AnnouncementDto.Projection)
            .ToListAsync(cancellationToken);
        return await AnnouncementDto.WithAuthorsAsync(current, identityService, cancellationToken);
    }

    public async Task<IReadOnlyList<AnnouncementDto>> Handle(GetAllAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        var all = await db.Announcements
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxListed)
            .Select(AnnouncementDto.Projection)
            .ToListAsync(cancellationToken);
        return await AnnouncementDto.WithAuthorsAsync(all, identityService, cancellationToken);
    }
}
