using MediatR;
using SmartDesk.Application.Features.ResourceBlocks.Common;

namespace SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;

// For the calendar, next to GET /api/reservations with the same range: blocks overlapping
// [From, To), optionally for one resource. Visible to everyone, since they explain why a
// slot can't be booked.
public sealed record GetResourceBlocksQuery(DateTime From, DateTime To, Guid? ResourceId = null)
    : IRequest<IReadOnlyList<ResourceBlockDto>>;
