using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;

// Active resources with no confirmed reservation overlapping [StartDateTime, EndDateTime).
// A read-only search: it does not reserve anything, and CreateReservationCommand re-checks
// the same rule at write time, so a slot shown as free here can still be taken by someone
// faster - the create call is the one that decides.
public sealed record GetAvailableResourcesQuery(
    DateTime StartDateTime,
    DateTime EndDateTime,
    Guid? ResourceTypeId = null,
    int? MinCapacity = null) : IRequest<IReadOnlyList<ResourceDto>>;
