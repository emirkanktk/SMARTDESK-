using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.CancelReservation;

public sealed record CancelReservationCommand(Guid Id) : IRequest<ReservationDto>;
