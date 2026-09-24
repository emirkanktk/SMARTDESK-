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
    IReadOnlyList<string> Features)
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
        r.Features.OrderBy(f => f.Name).Select(f => f.Name).ToList());
}
