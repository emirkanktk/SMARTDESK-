using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

// The owner is always the caller (taken from the token), never a field in the body.
public sealed record CreateReservationCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note) : IRequest<ReservationDto>;
