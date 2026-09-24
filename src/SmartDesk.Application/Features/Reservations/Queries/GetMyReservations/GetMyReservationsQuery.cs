using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;

// All of the caller's reservations - upcoming, past and cancelled - oldest start first. The
// My Reservations screen splits them into active/past itself.
public sealed record GetMyReservationsQuery : IRequest<IReadOnlyList<ReservationDto>>;
