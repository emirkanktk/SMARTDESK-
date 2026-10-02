using MediatR;
using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;

// A booking that repeats (ADR-0035): the first date as for any booking, and how it comes back.
// Every date gets the note, the reminder and the people invited. SkipConflicts: book the dates
// that are free and skip the others; without it, one date that isn't free stops the lot (409,
// code series-conflicts), so nothing is booked that the booker didn't see coming.
public sealed record CreateReservationSeriesCommand(
    Guid ResourceId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    string? Note,
    Recurrence Recurrence,
    int? ReminderMinutes = null,
    IReadOnlyList<Guid>? AttendeeIds = null,
    bool SkipConflicts = false) : IRequest<ReservationSeriesResultDto>;
