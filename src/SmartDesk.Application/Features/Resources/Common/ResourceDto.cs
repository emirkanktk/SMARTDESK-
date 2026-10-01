using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Common;

public sealed record ResourceDto(
    Guid Id,
    string Name,
    Guid ResourceTypeId,
    string ResourceTypeName,
    int Capacity,
    string Location,
    ResourceStatus Status,
    string? Description,
    IReadOnlyList<string> Features,
    IReadOnlyList<ResourceTeamDto> Teams,
    DateTime? PhotoUpdatedAt,
    bool CheckInRequired)
{
    // An expression, not a method, so EF translates it into the SELECT (type name and
    // features in the same query) instead of loading whole entities and mapping in memory.
    public static readonly Expression<Func<Resource, ResourceDto>> Projection = r => new ResourceDto(
        r.Id,
        r.Name,
        r.ResourceTypeId,
        r.ResourceType.Name,
        r.Capacity,
        r.Location,
        r.Status,
        r.Description,
        r.Features.OrderBy(f => f.Name).Select(f => f.Name).ToList(),
        // Named by ResourceReader: the managers' accounts are in Identity.
        r.Teams.Select(t => new ResourceTeamDto(t.TeamId, string.Empty)).ToList(),
        // Only when it changed, never the photo itself: a join on the key, no image bytes.
        r.Photo != null ? r.Photo.UpdatedAt : null,
        // Whether bookings must be checked in (ADR-0030); the QR code's secret is never here.
        r.CheckInRequired);
}

// A team a resource belongs to (ADR-0022): the Manager's account id and name. No teams means
// the resource is shared by everyone.
public sealed record ResourceTeamDto(Guid Id, string ManagerName)
{
    public const string UnknownManager = "Unknown manager";
}
