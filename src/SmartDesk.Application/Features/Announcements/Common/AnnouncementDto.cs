using System.Linq.Expressions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Announcements.Common;

// An announcement as the app shows it (ADR-0065). AuthorName: who posted it, filled afterwards
// with WithAuthorsAsync, since accounts live in Identity; null once that account is gone.
public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string? Body,
    AnnouncementTone Tone,
    DateTime CreatedAt,
    DateTime EndsAt,
    Guid? AuthorId)
{
    public string? AuthorName { get; init; }

    public static readonly Expression<Func<Announcement, AnnouncementDto>> Projection =
        a => new AnnouncementDto(a.Id, a.Title, a.Body, a.Tone, a.CreatedAt, a.EndsAt, a.CreatedByUserId);

    // For one just written, compiled once (after Projection, which it needs).
    private static readonly Func<Announcement, AnnouncementDto> Map = Projection.Compile();

    public static AnnouncementDto From(Announcement announcement) => Map(announcement);

    public static async Task<IReadOnlyList<AnnouncementDto>> WithAuthorsAsync(
        IReadOnlyList<AnnouncementDto> announcements, IIdentityService identityService, CancellationToken cancellationToken)
    {
        var names = await identityService.GetDisplayNamesAsync(
            announcements.Select(a => a.AuthorId).OfType<Guid>().Distinct().ToList(), cancellationToken);
        return announcements
            .Select(a => a with { AuthorName = a.AuthorId is { } id ? names.GetValueOrDefault(id) : null })
            .ToList();
    }
}
