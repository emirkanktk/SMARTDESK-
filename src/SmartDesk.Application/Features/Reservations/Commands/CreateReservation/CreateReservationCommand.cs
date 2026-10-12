using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

// The owner is always the caller (taken from the token), never a field in the body.
// ReminderMinutes: remind the owner that many minutes before the start (5, 15, 30 or 60);
// null or omitted means no reminder (ADR-0018).
// AttendeeIds: the people to invite (ADR-0032). A Manager invites their team's members, an
// Admin anyone; each gets a message in their inbox.
public sealed record CreateReservationCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note,
    int? ReminderMinutes = null,
    IReadOnlyList<Guid>? AttendeeIds = null) : IRequest<ReservationDto>, IBookingRequest
{
    Guid? IBookingRequest.MovingReservationId => null;
}
