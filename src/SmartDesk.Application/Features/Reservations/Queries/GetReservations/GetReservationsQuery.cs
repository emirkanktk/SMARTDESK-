using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservations;

// The calendar: Confirmed reservations overlapping [From, To), optionally for one resource
// or one resource type. Visible to every signed-in user - seeing who has a room is the point
// of a shared office calendar. Cancelled reservations aren't returned; they free the slot.
public sealed record GetReservationsQuery(
    DateTime From,
    DateTime To,
    Guid? ResourceId = null,
    Guid? ResourceTypeId = null) : IRequest<IReadOnlyList<ReservationDto>>;
