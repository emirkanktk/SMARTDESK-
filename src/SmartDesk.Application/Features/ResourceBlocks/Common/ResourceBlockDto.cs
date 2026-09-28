using System.Linq.Expressions;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.ResourceBlocks.Common;

public sealed record ResourceBlockDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string Reason,
    Guid CreatedByUserId)
{
    public static readonly Expression<Func<ResourceBlock, ResourceBlockDto>> Projection = b => new ResourceBlockDto(
        b.Id,
        b.ResourceId,
        b.Resource.Name,
        b.StartDateTime,
        b.EndDateTime,
        b.Reason,
        b.CreatedByUserId);
}
