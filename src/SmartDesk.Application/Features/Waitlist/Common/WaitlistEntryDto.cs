using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Waitlist.Common;

// Someone's place on a waiting list (ADR-0043). Position: 1 when nobody waiting for any of the
// same time on that resource joined earlier.
public sealed record WaitlistEntryDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    string ResourceLocation,
    DateTime StartDateTime,
    DateTime EndDateTime,
    DateTime CreatedAt,
    int Position);

public static class WaitlistReader
{
    // As DTOs, each with its place in line among the entries still to come.
    public static IQueryable<WaitlistEntryDto> Read(IApplicationDbContext db, IQueryable<WaitlistEntry> entries, DateTime now) =>
        entries.Select(e => new WaitlistEntryDto(
            e.Id,
            e.ResourceId,
            e.Resource.Name,
            e.Resource.Location,
            e.StartDateTime,
            e.EndDateTime,
            e.CreatedAt,
            1 + db.WaitlistEntries.Count(ahead =>
                ahead.ResourceId == e.ResourceId
                && ahead.CreatedAt < e.CreatedAt
                && ahead.EndDateTime > now
                && ahead.StartDateTime < e.EndDateTime
                && e.StartDateTime < ahead.EndDateTime)));
}
