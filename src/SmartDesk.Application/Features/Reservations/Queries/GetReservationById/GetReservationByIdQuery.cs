using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Queries.GetReservationById;

// Any signed-in user, like the calendar that already shows every Confirmed reservation. For
// the edit form and for opening a reservation from a calendar entry or a live event.
public sealed record GetReservationByIdQuery(Guid Id) : IRequest<ReservationDto>;
