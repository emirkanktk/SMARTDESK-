namespace SmartDesk.Application.Features.Reservations.Common;

// A claim on a resource for a time: a new booking, or one being moved. When it clashes, the
// 409 says what would do instead (ADR-0042), worked out from these.
public interface IBookingRequest
{
    Guid ResourceId { get; }
    DateTime StartDateTime { get; }
    DateTime EndDateTime { get; }

    // Who else is coming: with the owner, how many the resource must hold.
    IReadOnlyList<Guid>? AttendeeIds { get; }

    // The booking being moved, whose own slot doesn't count as taken; null for a new one.
    Guid? MovingReservationId { get; }
}
